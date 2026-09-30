// swift-tools-version:5.10
import PackageDescription

let package = Package(
    name: "DiskWatch",
    platforms: [.macOS(.v14)],
    targets: [
        .executableTarget(
            name: "DiskWatch",
            path: "Sources/DiskWatch"
        )
    ]
)
