/**
 * Electron Forge Configuration
 * Configures installers and package options
 */

module.exports = {
    packagerConfig: {
        name: 'Memory Timeline',
        executableName: 'memory-timeline',
        icon: './assets/icon', // Will look for icon.icns on macOS, icon.ico on Windows, icon.png on Linux
        asar: true,
        appBundleId: 'com.memorytimeline.app',
        appCategoryType: 'public.app-category.productivity',
        win32metadata: {
            CompanyName: 'Memory Timeline',
            FileDescription: 'Personal Memory Timeline with AI',
            ProductName: 'Memory Timeline'
        },
        osxSign: {}, // Signing options for macOS (requires Apple Developer account)
        osxNotarize: undefined // Notarization options for macOS (requires Apple Developer account)
    },
    rebuildConfig: {},
    makers: [
        // Windows installer (Squirrel)
        {
            name: '@electron-forge/maker-squirrel',
            config: {
                name: 'memory_timeline',
                authors: 'Memory Timeline',
                description: 'Personal Memory Timeline Application with AI-powered event extraction',
                setupIcon: './assets/icon.ico',
                loadingGif: './assets/install-spinner.gif',
                iconUrl: 'https://raw.githubusercontent.com/yourusername/memory-timeline/main/assets/icon.ico',
                noMsi: true,
                setupExe: 'MemoryTimelineSetup.exe',
                certificateFile: process.env.WINDOWS_CERTIFICATE_FILE,
                certificatePassword: process.env.WINDOWS_CERTIFICATE_PASSWORD
            }
        },
        // macOS installer (DMG)
        {
            name: '@electron-forge/maker-dmg',
            config: {
                name: 'Memory Timeline',
                icon: './assets/icon.icns',
                background: './assets/dmg-background.png',
                format: 'ULFO'
            }
        },
        // Zip file (cross-platform)
        {
            name: '@electron-forge/maker-zip',
            platforms: ['darwin', 'linux', 'win32']
        },
        // Debian package (Linux)
        {
            name: '@electron-forge/maker-deb',
            config: {
                options: {
                    maintainer: 'Memory Timeline',
                    homepage: 'https://github.com/yourusername/memory-timeline',
                    name: 'memory-timeline',
                    productName: 'Memory Timeline',
                    genericName: 'Memory Timeline',
                    description: 'Personal Memory Timeline Application with AI-powered event extraction',
                    categories: ['Office', 'Utility'],
                    icon: './assets/icon.png',
                    section: 'utils',
                    priority: 'optional',
                    depends: []
                }
            }
        },
        // RPM package (Linux)
        {
            name: '@electron-forge/maker-rpm',
            config: {
                options: {
                    name: 'memory-timeline',
                    productName: 'Memory Timeline',
                    genericName: 'Memory Timeline',
                    description: 'Personal Memory Timeline Application with AI-powered event extraction',
                    categories: ['Office', 'Utility'],
                    icon: './assets/icon.png',
                    homepage: 'https://github.com/yourusername/memory-timeline',
                    license: 'MIT'
                }
            }
        }
    ],
    plugins: [
        {
            name: '@electron-forge/plugin-auto-unpack-natives',
            config: {}
        }
    ]
};
