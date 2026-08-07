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
/// Service "com.memoryline.companion.tokens", one item per token under the
/// accounts "access" and "refresh". Items use
/// `kSecAttrAccessibleAfterFirstUnlock` so background upload retries can read
/// them while the phone is locked (after the first unlock since boot).
///
/// Token values are never logged — only account names and status codes (§14.5).
final class KeychainTokenStore: TokenStore {
    private let service = "com.memoryline.companion.tokens"
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

    /// Base query identifying one token item.
    private func baseQuery(account: String) -> [String: Any] {
        [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecAttrAccount as String: account
        ]
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
