import Foundation
import Security
import os

/// Error thrown when a Keychain write fails. Carries the OSStatus and Apple's
/// description of it — never the secret being stored.
struct KeychainError: Error, LocalizedError, Sendable {
    var status: OSStatus
    var operation: String

    var errorDescription: String? {
        let detail = SecCopyErrorMessageString(status, nil) as String? ?? "OSStatus \(status)"
        return "Keychain \(operation) failed: \(detail)"
    }
}

/// `TokenStore` over `kSecClassGenericPassword` items (design §14.1/§14.3).
/// One item per token under the accounts "access" and "refresh". Items use
/// `kSecAttrAccessibleAfterFirstUnlock` so background upload retries can read
/// them while the phone is locked (after the first unlock since boot).
///
/// Token values are never logged — only account names and status codes (§14.5).
final class KeychainTokenStore: TokenStore {
    /// Keychain service name, derived from the running app's bundle
    /// identifier rather than hardcoded.
    ///
    /// On iOS this evaluates to "ca.fluxology.memoryline.ios.tokens" — byte
    /// for byte the literal this replaced — so existing installs keep reading
    /// the tokens they already stored; there is no migration.
    ///
    /// The Mac app gets "ca.fluxology.memoryline.mac.tokens" and therefore its
    /// own credentials, which is the intended behaviour: each device pairs
    /// with the sync service independently and holds its own device-bound
    /// token pair, so sharing one item between them would be wrong.
    private let service: String = {
        // A bundle identifier is always present in an app; the fallback keeps
        // the historical iOS value for any host that lacks one.
        let bundleId = Bundle.main.bundleIdentifier ?? "ca.fluxology.memoryline.ios"
        return "\(bundleId).tokens"
    }()

    private let logger = AppLog.security

    private enum Account {
        static let access = "access"
        static let refresh = "refresh"
    }

    func accessToken() -> String? {
        read(account: Account.access)
    }

    func refreshToken() -> String? {
        read(account: Account.refresh)
    }

    func save(accessToken: String, refreshToken: String) throws {
        try write(accessToken, account: Account.access)
        try write(refreshToken, account: Account.refresh)
    }

    func clear() {
        delete(account: Account.access)
        delete(account: Account.refresh)
    }

    // MARK: - Keychain primitives

    /// Base query identifying one token item. Every read, write and delete
    /// builds on this, so anything that must be consistent across operations
    /// belongs here.
    private func baseQuery(account: String) -> [String: Any] {
        var query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecAttrAccount as String: account
        ]

        #if os(macOS)
        // macOS has two keychain implementations. Without this flag,
        // SecItem* calls land in the legacy file-based keychain, where
        // kSecAttrAccessible is not honoured the way it is on iOS and items
        // are not protected by the Secure Enclave. Opting into the data
        // protection keychain gives the Mac the same semantics the iOS
        // companion already relies on; it requires the app to declare a
        // keychain-access-group entitlement, which macos-native does.
        //
        // Guarded rather than set unconditionally: on iOS the key is
        // documented as ignored, but this store holds the credentials that a
        // paired phone needs, and a lookup that silently stopped matching
        // would unpair every existing install. Not worth the risk for a flag
        // that does nothing there.
        query[kSecUseDataProtectionKeychain as String] = true
        #endif

        return query
    }

    private func read(account: String) -> String? {
        var query = baseQuery(account: account)
        query[kSecReturnData as String] = true
        query[kSecMatchLimit as String] = kSecMatchLimitOne
        var result: CFTypeRef?
        let status = SecItemCopyMatching(query as CFDictionary, &result)
        guard status == errSecSuccess, let data = result as? Data else {
            if status != errSecSuccess && status != errSecItemNotFound {
                logger.error("Keychain read for \(account, privacy: .public) failed: \(status)")
            }
            return nil
        }
        return String(data: data, encoding: .utf8)
    }

    /// Add-or-update: probe with SecItemCopyMatching, then SecItemUpdate an
    /// existing item or SecItemAdd a new one.
    private func write(_ value: String, account: String) throws {
        let data = Data(value.utf8)
        let base = baseQuery(account: account)
        let probeStatus = SecItemCopyMatching(base as CFDictionary, nil)
        switch probeStatus {
        case errSecSuccess:
            let update: [String: Any] = [
                kSecValueData as String: data,
                kSecAttrAccessible as String: kSecAttrAccessibleAfterFirstUnlock
            ]
            let status = SecItemUpdate(base as CFDictionary, update as CFDictionary)
            guard status == errSecSuccess else {
                logger.error("Keychain update for \(account, privacy: .public) failed: \(status)")
                throw KeychainError(status: status, operation: "update")
            }
        case errSecItemNotFound:
            var add = base
            add[kSecValueData as String] = data
            add[kSecAttrAccessible as String] = kSecAttrAccessibleAfterFirstUnlock
            let status = SecItemAdd(add as CFDictionary, nil)
            guard status == errSecSuccess else {
                logger.error("Keychain add for \(account, privacy: .public) failed: \(status)")
                throw KeychainError(status: status, operation: "add")
            }
        default:
            logger.error("Keychain probe for \(account, privacy: .public) failed: \(probeStatus)")
            throw KeychainError(status: probeStatus, operation: "lookup")
        }
    }

    private func delete(account: String) {
        let status = SecItemDelete(baseQuery(account: account) as CFDictionary)
        if status != errSecSuccess && status != errSecItemNotFound {
            logger.error("Keychain delete for \(account, privacy: .public) failed: \(status)")
        }
    }
}
