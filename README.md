# Storage Optimizer

A native macOS app that shows where your disk space went and cleans caches safely.

## Features

- **Overview**: a disk usage bar like System Settings › Storage, broken down by file type. It also shows how much space caches are using and lists your largest folders and files.
- **Large Files**: a sortable table of the 2,000 largest files and app bundles.
  - Filter by size (10 MB+ … 1 GB+), by type, by name, or to files not opened in 6 months.
  - Show items in Finder, open them, copy their paths or move them to the Trash.
- **Folders**: browse the scanned folder tree, sorted by size, with a size bar and percentage per item. Files under 1 MB in each folder are grouped into one row.
- **File Types**: space per category (Videos, Images, Audio, Documents, Archives, Disk Images, Applications, Developer, Other), with the largest files and file extensions in each.
- **Cache Cleaner**: finds and measures caches that tools and apps rebuild by themselves, then deletes the ones you select:
  - Xcode DerivedData, Previews, Device Support files, Simulator caches and unavailable simulators
  - Swift Package Manager, CocoaPods, Carthage
  - Homebrew, npm, Yarn, pnpm, Bun, pip, Gradle, Go and Cargo caches
  - `~/Library/Caches`, `~/Library/Logs` and the Trash
  - Project build folders found by a scan (`node_modules`, `Pods`, `.build`, `DerivedData`, `target`, virtualenvs, `.next`)

You can scan your **home folder**, the **entire disk**, or **any folder** you choose.

## Requirements

- macOS 14 Sonoma or later
- Xcode 16 or later (or the matching Command Line Tools) to build it yourself

## Getting the app

### Download a build

Every push runs the **CI** workflow on GitHub Actions. To download the app:

1. Open the latest successful run on the repository's **Actions** tab.
2. Download the `StorageOptimizer` artifact, unzip it, and move **Storage Optimizer.app** to `/Applications`.

The app is signed ad hoc rather than with a Developer ID, so macOS quarantines it after download. Clear the quarantine once:

```sh
xattr -dr com.apple.quarantine "/Applications/Storage Optimizer.app"
```

Alternatively, right-click the app, choose **Open**, then confirm in **System Settings › Privacy & Security**.

### Build it yourself

```sh
git clone https://github.com/ugokce/MacOsStorageOptimizer.git
cd MacOsStorageOptimizer

swift run StorageOptimizer          # quick start from Terminal
./scripts/build_app.sh              # creates "build/Storage Optimizer.app"
open "build/Storage Optimizer.app"
```

- `./scripts/build_app.sh --universal` builds for both Apple silicon and Intel (this needs Xcode).
- You can also open `Package.swift` in Xcode and run the `StorageOptimizer` scheme.

To run the unit tests:

```sh
swift test
```

## Full Disk Access

macOS hides some folders (Mail, Messages, Safari and other apps' containers) unless an app has **Full Disk Access**. Without it, those folders can't be measured or cleaned, and the app shows a banner. To grant it:

1. Open **System Settings › Privacy & Security › Full Disk Access**. The banner's **Open Settings** button goes there directly.
2. Add **Storage Optimizer**. If you start the app with `swift run`, add **Terminal** instead.
3. Quit and reopen the app.

A rebuilt app counts as a new app to macOS, so you may need to grant Full Disk Access again after rebuilding.

## How deletion works

- **Files and folders** you remove from Large Files, Folders or File Types go to the **Trash**, so you can restore them. Their space is freed when you empty the Trash; the Cache Cleaner can do that too.
- **Caches** are deleted permanently after you confirm, because moving them to the Trash wouldn't free any space.
  - Only the contents of each cache folder are deleted; the folders themselves stay.
  - When one cache folder sits inside another (for example Homebrew's cache inside `~/Library/Caches`), only its own target cleans it.
- **Recommended vs. Review First:** recommended targets are selected by default. Targets marked **Review First** are never selected automatically: Xcode Archives (which hold dSYMs for released builds), iPhone backups, the pnpm store and the Trash.
- **Safety check:** every delete or move to the Trash first passes a check that only allows items inside your home folder, `/Applications`, `/Users/Shared` or external volumes. It always refuses system locations and the standard folders themselves (Documents, Library, Caches, ...).
- **Quit apps first:** quit Xcode before cleaning its caches, and close other apps before cleaning App Caches.

## What "System Data" and "Other Users & Shared" mean

The macOS Storage panel puts many things under **System Data**, including caches, logs, simulators, Time Machine local snapshots, VM swap and app support files.

- Scanning the home folder and using the Cache Cleaner covers the parts you own.
- An **Entire Disk** scan shows the rest of what your user account can read.
- Folders that belong to other users, and system-protected locations, are counted as "couldn't be read".

Some space this app can't clean:

- **Time Machine local snapshots:** list them with `tmutil listlocalsnapshots /`. macOS deletes them automatically when space runs low.
- **Other user accounts:** their files show up as **Other Users & Shared**. Clean them up while logged in as that user.

## Project layout

```
Package.swift
Sources/StorageCore/        Scanner, statistics, cache catalog, cleaner and deletion guard (Foundation only)
Sources/StorageOptimizer/   SwiftUI app
Tests/StorageCoreTests/     Unit tests for StorageCore
scripts/build_app.sh        Builds and signs the .app bundle
.github/workflows/ci.yml    Builds, tests and packages the app on macOS
```

## License

MIT. See [LICENSE](LICENSE).
