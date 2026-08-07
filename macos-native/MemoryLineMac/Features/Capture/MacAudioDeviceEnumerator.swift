import CoreAudio
import Foundation
import Observation
import os

/// Logger for this file.
///
/// File-scoped rather than the house `private let logger = ...` instance
/// property, because the CoreAudio helpers below are `nonisolated static` (see
/// `audioDeviceID(forUID:)`) and cannot reach an instance member. `Logger` is
/// `Sendable` and stateless, so one shared value is safe from any isolation.
private let audioLog = AppLog.logger(category: "audio")

/// Lists the Mac's audio **input** devices and keeps that list live while
/// hardware comes and goes (port plan §4.1).
///
/// The iPhone has one microphone and no device picker, so there is no iOS type
/// to share or mirror here — this is genuinely new surface. Two decisions in it
/// are worth understanding before changing anything:
///
///  - **Identity is the CoreAudio device UID, never the `AudioDeviceID`.** The
///    numeric ID is an index into the HAL's live object table: it is reassigned
///    across reboots, and a device that is unplugged and replugged commonly
///    comes back under a different number. `MacCaptureSettingsKey.inputDeviceId`
///    persists whatever we put in `MacAudioInputDevice.id`, so persisting the
///    number would silently point the recorder at *some other microphone* after
///    a reboot. The UID is stable and is what CoreAudio itself uses for exactly
///    this purpose. `audioDeviceID(forUID:)` converts back when the recorder
///    needs a number.
///  - **A device is an input only if it actually carries input channels.** Every
///    device in `kAudioHardwarePropertyDevices` is a candidate — speakers,
///    HDMI displays, virtual loopbacks — and the only reliable discriminator is
///    a non-empty `kAudioDevicePropertyStreamConfiguration` on the *input*
///    scope. Filtering on the name, or on the device's transport type, gets
///    both false positives (output-only USB interfaces) and false negatives
///    (aggregate and loopback devices users legitimately record from).
///
/// Failures are absorbed per device: an unreadable property skips that one
/// device and the enumeration continues. A machine with one flaky driver must
/// still be able to pick its built-in microphone.
@MainActor
@Observable
final class MacAudioDeviceEnumerator: MacAudioDeviceEnumerating {

    // MARK: - Observable state

    /// Connected inputs, system default first, then Finder-style by name.
    /// Empty until `refresh()` or `startObserving()` runs — construction does
    /// no HAL I/O, so building this in a composition root stays cheap.
    private(set) var devices: [MacAudioInputDevice] = []

    // MARK: - Observation lifecycle

    /// Guards `startObserving()` against double registration. Not derived from
    /// `registeredAddresses` being non-empty, because a partial registration
    /// (one of the two properties failing) still counts as observing.
    @ObservationIgnored private var isObserving = false

    /// The block handed to CoreAudio. Held so `stopObserving()` can pass the
    /// same value back to `AudioObjectRemovePropertyListenerBlock`, which
    /// matches registrations on (object, address, block).
    @ObservationIgnored private var hardwareListener: AudioObjectPropertyListenerBlock?

    /// Exactly the addresses whose registration returned `noErr`. Removing only
    /// these is what makes the teardown safe: we never ask CoreAudio to remove a
    /// listener we did not manage to add.
    @ObservationIgnored private var registeredAddresses: [AudioObjectPropertyAddress] = []

    /// Incremented on every start and every stop. Each listener block captures
    /// the value it was registered under and drops its callback if the counter
    /// has moved on, so a notification already queued when `stopObserving()`
    /// runs cannot repopulate `devices` after the owner asked us to stop.
    @ObservationIgnored private var observationGeneration = 0

    /// Does nothing but exist. Reading the device list is HAL IPC and belongs at
    /// a point the caller chose, not inside a composition root's `init`.
    init() {}

    // No `deinit` removing the listeners, deliberately, and for the same reason
    // `MacSyncCoordinator` has no deinit cancelling its ticker: `deinit` on a
    // `@MainActor` class is nonisolated and may not touch this object's
    // main-actor state. The owner calls `stopObserving()`. If it never does, the
    // registration outlives the object but is inert — the block holds `self`
    // weakly, so it cannot keep the enumerator alive and fires into nothing.

    // MARK: - MacAudioDeviceEnumerating

    /// Re-reads the hardware and publishes the result.
    ///
    /// Assignment is skipped when the list is unchanged. `@Observable`
    /// invalidates every view bound to `devices` on *any* assignment, and
    /// hardware events routinely fire for things that do not change the input
    /// list at all (an output-only display waking up, an unrelated default
    /// *output* change), so an unconditional write would churn the picker.
    func refresh() {
        let current = Self.enumerateInputDevices()
        guard current != devices else { return }
        devices = current
        let hasDefault = current.contains { $0.isSystemDefault }
        audioLog.info("input devices refreshed count=\(current.count) hasDefault=\(hasDefault)")
    }

    /// Registers CoreAudio listeners so the list tracks headsets, docks and
    /// virtual devices appearing and disappearing, then reads the list once.
    ///
    /// Idempotent: calling this while already observing returns immediately. A
    /// second registration at the same address is a second, independent entry in
    /// CoreAudio's listener table, and `stopObserving()` — which removes each
    /// address once — would leave the duplicate behind firing forever.
    func startObserving() {
        guard !isObserving else { return }

        observationGeneration &+= 1
        let generation = observationGeneration

        // Both properties share one handler: it re-reads everything, so which
        // one fired is irrelevant. CoreAudio keys registrations on
        // (object, address, block), so the same block at two addresses is two
        // distinct registrations that must each be removed.
        let listener: AudioObjectPropertyListenerBlock = { [weak self] _, _ in
            // The `UnsafePointer<AudioObjectPropertyAddress>` argument is valid
            // only for the duration of this call — it is ignored rather than
            // captured, which is why the handler re-reads instead of acting on
            // the specific property that changed.
            //
            // Hop explicitly with `Task { @MainActor in }` rather than
            // `MainActor.assumeIsolated`. Running on `DispatchQueue.main` is not
            // something the compiler accepts as main-actor isolation, and
            // `assumeIsolated` would *trap* rather than fail to build if the
            // queue argument below were ever changed. This also gives the
            // generation check a natural home. It matches how the iOS
            // `AudioRecorderService` marshals its audio-session notifications.
            Task { @MainActor in
                guard let self, self.observationGeneration == generation else { return }
                self.refresh()
            }
        }

        var registered: [AudioObjectPropertyAddress] = []
        for address in [Self.deviceListAddress, Self.defaultInputDeviceAddress] {
            // `AudioObjectAddPropertyListenerBlock` takes `UnsafePointer`, so the
            // address needs to be a `var`; CoreAudio copies it, so a local is fine.
            var mutableAddress = address
            // `DispatchQueue.main`, not `nil` and not a private queue. Passing
            // `nil` delivers on a HAL-internal thread, which would put every line
            // of the handler on the wrong thread for a `@MainActor` type. A
            // private serial queue would work too but buys nothing: the handler's
            // only job is to schedule main-actor work. Delivering on the main
            // queue also serialises the device-list and default-input
            // notifications in the order CoreAudio emits them, so one plug event
            // that changes both produces two ordered refreshes rather than a race.
            let status = AudioObjectAddPropertyListenerBlock(
                AudioObjectID(kAudioObjectSystemObject),
                &mutableAddress,
                DispatchQueue.main,
                listener)
            if status == noErr {
                registered.append(address)
            } else {
                audioLog.error(
                    "could not observe audio hardware property selector=\(address.mSelector) status=\(Self.statusDescription(status), privacy: .public)")
            }
        }

        if registered.isEmpty {
            // Nothing was registered, so there is nothing to tear down and no
            // reason to claim otherwise: leaving `isObserving` false lets a later
            // call try again. The list below is still read once, so the picker is
            // populated — it just will not track changes.
            audioLog.error("audio hardware observation failed; the input list will not track hardware changes")
        } else {
            isObserving = true
            hardwareListener = listener
            registeredAddresses = registered
        }

        // Enumerate AFTER registering. A device that appears between the two
        // steps is then caught by the listener; enumerating first would open a
        // window in which an arrival is missed until the next hardware event.
        refresh()
    }

    /// Removes the listeners registered by `startObserving()`. Safe to call when
    /// not observing, and safe to call twice.
    func stopObserving() {
        // Bump first. Any callback already sitting on the main queue for the
        // registrations about to be dropped now sees a stale generation and
        // returns without touching `devices`.
        observationGeneration &+= 1

        guard let listener = hardwareListener else {
            isObserving = false
            registeredAddresses.removeAll()
            return
        }

        for address in registeredAddresses {
            var mutableAddress = address
            let status = AudioObjectRemovePropertyListenerBlock(
                AudioObjectID(kAudioObjectSystemObject),
                &mutableAddress,
                DispatchQueue.main,
                listener)
            if status != noErr {
                // Logged, not thrown, and not retried. CoreAudio matches removal
                // on the block *object*, and Swift bridges a closure to an
                // Objective-C block at the call boundary — it does not promise to
                // hand back the same block object when the same stored closure is
                // bridged a second time. So this can fail with correct arguments.
                // That is why the generation check in the handler is load-bearing
                // rather than belt-and-braces: a registration that survives this
                // call is inert, and a subsequent `startObserving()` installs a
                // fresh generation the stale block can never match.
                audioLog.notice(
                    "audio property listener removal returned selector=\(address.mSelector) status=\(Self.statusDescription(status), privacy: .public)")
            }
        }

        isObserving = false
        registeredAddresses.removeAll()
        hardwareListener = nil
    }

    // MARK: - UID → AudioDeviceID

    /// Resolves a persisted `MacAudioInputDevice.id` (a CoreAudio device UID)
    /// back to the numeric `AudioDeviceID` the recorder must hand to
    /// `kAudioOutputUnitProperty_CurrentDevice` / `AVAudioEngine`'s input node.
    /// Returns nil when the UID names nothing currently connected, or names a
    /// device that no longer has input channels — both cases mean the same thing
    /// to the caller, which should fall back to the system default input
    /// (`MacCaptureSettingsKey.inputDeviceId` documents that fallback as the
    /// intended behaviour when the chosen device is unplugged).
    ///
    /// `nonisolated` because it reads nothing but CoreAudio: the recorder may
    /// well resolve the device while configuring its engine off the main actor,
    /// and there is nothing main-actor about a HAL property read.
    ///
    /// This is a linear scan rather than `kAudioHardwarePropertyTranslateUIDToDevice`
    /// on purpose. The translate property needs the UID passed as *qualifier*
    /// data — a shape used nowhere else in this file — whereas the scan reuses
    /// exactly the two reads the enumeration above already depends on. If those
    /// work, this works. The device count on a Mac is single digits.
    nonisolated static func audioDeviceID(forUID uid: String) -> AudioDeviceID? {
        for deviceID in allDeviceIDs() {
            guard stringProperty(kAudioDevicePropertyDeviceUID, of: deviceID) == uid else { continue }
            guard inputChannelCount(of: deviceID) > 0 else { return nil }
            return deviceID
        }
        return nil
    }

    // MARK: - Property addresses

    // Declared once each so the add and the remove cannot drift apart: a removal
    // whose address differs from the registration's by one field silently does
    // nothing. `kAudioObjectPropertyElementMain` replaced the old
    // `...ElementMaster` spelling in macOS 12; this target is macOS 14.

    nonisolated private static var deviceListAddress: AudioObjectPropertyAddress {
        AudioObjectPropertyAddress(
            mSelector: kAudioHardwarePropertyDevices,
            mScope: kAudioObjectPropertyScopeGlobal,
            mElement: kAudioObjectPropertyElementMain)
    }

    nonisolated private static var defaultInputDeviceAddress: AudioObjectPropertyAddress {
        AudioObjectPropertyAddress(
            mSelector: kAudioHardwarePropertyDefaultInputDevice,
            mScope: kAudioObjectPropertyScopeGlobal,
            mElement: kAudioObjectPropertyElementMain)
    }

    // MARK: - CoreAudio reads

    /// Builds the published list: every device with at least one input channel,
    /// default first.
    nonisolated private static func enumerateInputDevices() -> [MacAudioInputDevice] {
        // Read once, outside the loop: the default can only be one device, and
        // re-reading it per device would multiply HAL round trips for nothing.
        let defaultInputID = defaultInputDeviceID()

        var result: [MacAudioInputDevice] = []
        for deviceID in allDeviceIDs() {
            guard inputChannelCount(of: deviceID) > 0 else { continue }

            // No UID means nothing durable to persist or to resolve later, so the
            // device is unusable to us even if it records fine. In practice every
            // real device has one; a driver that omits it is broken.
            guard let uid = stringProperty(kAudioDevicePropertyDeviceUID, of: deviceID) else {
                audioLog.notice("skipping input device with no UID")
                continue
            }

            // `kAudioDevicePropertyDeviceNameCFString` is an alias for the same
            // four-char code as `kAudioObjectPropertyName`, so there is no second
            // selector worth trying; the UID is the real fallback, and it is at
            // least recognisable ("AppleUSBAudioEngine:...").
            let name = stringProperty(kAudioObjectPropertyName, of: deviceID) ?? uid

            result.append(MacAudioInputDevice(
                id: uid,
                name: name,
                isSystemDefault: deviceID == defaultInputID))
        }

        result.sort { lhs, rhs in
            // Default first — it is what the app uses when nothing is persisted,
            // so it should be the first thing the user sees.
            if lhs.isSystemDefault != rhs.isSystemDefault { return lhs.isSystemDefault }
            // `localizedStandardCompare` is the Finder's ordering: case- and
            // diacritic-insensitive, and it sorts "Mic 2" before "Mic 10".
            let byName = lhs.name.localizedStandardCompare(rhs.name)
            if byName != .orderedSame { return byName == .orderedAscending }
            // Two identical USB mics really do report identical names. Break on
            // the UID so the order is stable across refreshes instead of
            // shuffling whenever CoreAudio returns the devices in a new order.
            return lhs.id < rhs.id
        }
        return result
    }

    /// Every audio object the HAL currently knows about, inputs and outputs
    /// alike. Empty on failure.
    nonisolated private static func allDeviceIDs() -> [AudioDeviceID] {
        var address = deviceListAddress

        var dataSize: UInt32 = 0
        let sizeStatus = AudioObjectGetPropertyDataSize(
            AudioObjectID(kAudioObjectSystemObject), &address, 0, nil, &dataSize)
        guard sizeStatus == noErr else {
            audioLog.error("device list size query failed status=\(statusDescription(sizeStatus), privacy: .public)")
            return []
        }

        // `stride`, not `size`: this is array element spacing. They are both 4
        // for a UInt32, but the distinction is the one that stays correct.
        let elementStride = MemoryLayout<AudioDeviceID>.stride
        let capacity = Int(dataSize) / elementStride
        guard capacity > 0 else { return [] }

        var deviceIDs = [AudioDeviceID](repeating: AudioObjectID(kAudioObjectUnknown), count: capacity)
        let status = AudioObjectGetPropertyData(
            AudioObjectID(kAudioObjectSystemObject), &address, 0, nil, &dataSize, &deviceIDs)
        guard status == noErr else {
            audioLog.error("device list read failed status=\(statusDescription(status), privacy: .public)")
            return []
        }

        // `dataSize` is in/out: the read reports how much it actually wrote,
        // which can be less than the size query promised if a device disappeared
        // between the two calls. Trust the smaller number rather than handing
        // back uninitialised zeros as if they were device IDs.
        let returned = Int(dataSize) / elementStride
        return Array(deviceIDs.prefix(min(capacity, max(0, returned))))
    }

    /// The device the system currently treats as default input, or nil when
    /// there is none (a Mac with every input unplugged and no built-in mic).
    nonisolated private static func defaultInputDeviceID() -> AudioDeviceID? {
        var address = defaultInputDeviceAddress
        var deviceID = AudioObjectID(kAudioObjectUnknown)
        var dataSize = UInt32(MemoryLayout<AudioDeviceID>.size)

        let status = AudioObjectGetPropertyData(
            AudioObjectID(kAudioObjectSystemObject), &address, 0, nil, &dataSize, &deviceID)
        guard status == noErr else {
            audioLog.notice("default input query failed status=\(statusDescription(status), privacy: .public)")
            return nil
        }
        // The HAL answers `noErr` with `kAudioObjectUnknown` when there is no
        // default input, so the status alone is not enough.
        guard deviceID != AudioObjectID(kAudioObjectUnknown) else { return nil }
        return deviceID
    }

    /// Total channels across the device's input streams. Zero means "not an
    /// input" — that is the whole filter, and it is also what an unreadable
    /// device reports, which is the behaviour we want either way.
    nonisolated private static func inputChannelCount(of deviceID: AudioDeviceID) -> Int {
        var address = AudioObjectPropertyAddress(
            mSelector: kAudioDevicePropertyStreamConfiguration,
            mScope: kAudioObjectPropertyScopeInput,
            mElement: kAudioObjectPropertyElementMain)

        // This property is a variable-length `AudioBufferList` (a count followed
        // by a flexible array), so the size has to be asked for before the read.
        var dataSize: UInt32 = 0
        guard AudioObjectGetPropertyDataSize(deviceID, &address, 0, nil, &dataSize) == noErr else {
            return 0
        }

        // A device with no input streams answers with just the list header
        // (`mNumberBuffers == 0`), which is *smaller* than Swift's
        // `AudioBufferList` struct. There is nothing to count, and reading the
        // short answer as an `AudioBufferList` would read past the allocation.
        guard dataSize >= UInt32(MemoryLayout<AudioBufferList>.size) else { return 0 }

        let raw = UnsafeMutableRawPointer.allocate(
            byteCount: Int(dataSize),
            alignment: MemoryLayout<AudioBufferList>.alignment)
        defer { raw.deallocate() }

        guard AudioObjectGetPropertyData(deviceID, &address, 0, nil, &dataSize, raw) == noErr else {
            return 0
        }

        // `bindMemory`, not `assumingMemoryBound`: this allocation was raw and is
        // being given a type for the first time. Capacity 1 covers the header;
        // `UnsafeMutableAudioBufferListPointer` walks the trailing flexible array
        // itself, which is the standard way to read this property and the reason
        // the buffer was sized from `dataSize` rather than from the struct.
        let list = UnsafeMutableAudioBufferListPointer(raw.bindMemory(to: AudioBufferList.self, capacity: 1))
        return list.reduce(0) { $0 + Int($1.mNumberChannels) }
    }

    /// Reads a CFString-valued property (UID, name). Returns nil rather than
    /// throwing: a device that will not answer is a device we skip, never a
    /// failed enumeration.
    nonisolated private static func stringProperty(
        _ selector: AudioObjectPropertySelector,
        of objectID: AudioObjectID
    ) -> String? {
        var address = AudioObjectPropertyAddress(
            mSelector: selector,
            mScope: kAudioObjectPropertyScopeGlobal,
            mElement: kAudioObjectPropertyElementMain)

        // CoreAudio writes a **+1 retained** `CFStringRef` here — the caller owns
        // it. Landing it in an `Unmanaged` makes that hand-off explicit and
        // `takeRetainedValue()` consumes the retain. Writing straight into a
        // `CFString?` also happens to balance, but only by accident of where ARC
        // chooses to insert its release, which is not a thing to rely on.
        var value: Unmanaged<CFString>?
        // A CFStringRef out-parameter is one pointer wide.
        var dataSize = UInt32(MemoryLayout<CFString>.size)

        let status = withUnsafeMutablePointer(to: &value) { pointer in
            AudioObjectGetPropertyData(
                objectID, &address, 0, nil, &dataSize, UnsafeMutableRawPointer(pointer))
        }
        guard status == noErr, let value else { return nil }
        return value.takeRetainedValue() as String
    }

    /// Renders an `OSStatus` for the log. CoreAudio's errors are four-character
    /// codes (`'!obj'`, `'who?'`, `'nope'`) whose decimal forms are unreadable in
    /// Console, and a status is the only diagnostic these calls give us.
    nonisolated private static func statusDescription(_ status: OSStatus) -> String {
        let bytes: [UInt8] = [
            UInt8(truncatingIfNeeded: status >> 24),
            UInt8(truncatingIfNeeded: status >> 16),
            UInt8(truncatingIfNeeded: status >> 8),
            UInt8(truncatingIfNeeded: status)
        ]
        guard bytes.allSatisfy({ $0 >= 0x20 && $0 <= 0x7E }) else { return String(status) }
        return "'\(String(decoding: bytes, as: UTF8.self))' (\(status))"
    }
}
