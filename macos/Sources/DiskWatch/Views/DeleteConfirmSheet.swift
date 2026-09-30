import SwiftUI

/// Confirmation for Trash/permanent deletion. Protected items are listed but skipped; dangerous
/// items require typing DELETE; permanent deletion requires an explicit acknowledgment.
struct DeleteConfirmView: View {
    @Environment(AppState.self) private var app
    let request: DeletionRequest
    @State private var permanent: Bool
    @State private var typed = ""
    @State private var acknowledged = false
    @State private var working = false
    @State private var result: DeletionResult?

    init(request: DeletionRequest) {
        self.request = request
        _permanent = State(initialValue: request.permanent)
    }

    private var canProceed: Bool {
        guard !request.allowed.isEmpty, !working else { return false }
        if request.needsTypedConfirmation && typed != "DELETE" { return false }
        if permanent && !acknowledged { return false }
        return true
    }

    var body: some View {
        VStack(alignment: .leading, spacing: FN.s5) {
            if let result { resultView(result) } else { confirmView }
        }
    }

    // MARK: Confirm

    private var confirmView: some View {
        let allowed = request.allowed
        let blocked = request.blocked
        let worst = allowed.map(\.verdict.level).max() ?? .safe
        let noun = "\(allowed.count) item\(allowed.count == 1 ? "" : "s")"
        return VStack(alignment: .leading, spacing: FN.s5) {
            FNSectionHeading(title: permanent ? "Delete permanently" : "Move to Trash",
                             caption: "\(noun) · \(Fmt.bytes(request.totalAllowed)) · from \(request.source)") {
                HStack(spacing: 6) {
                    ForEach(SafetyLevel.allCases.reversed()) { lvl in
                        let n = request.items.filter { $0.verdict.level == lvl }.count
                        if n > 0 { FNPill(text: "\(n) \(lvl.shortTitle)", state: lvl.pill) }
                    }
                }
            }

            ScrollView {
                VStack(alignment: .leading, spacing: 0) {
                    ForEach(allowed.sorted { $0.verdict.level > $1.verdict.level }) { item in itemRow(item) }
                    if !blocked.isEmpty {
                        HStack(spacing: FN.s1) {
                            Text("Skipped — protected").fnLabel(FN.danger)
                            Spacer()
                        }
                        .padding(.horizontal, FN.s2).padding(.top, FN.s3).padding(.bottom, FN.s1)
                        ForEach(blocked) { item in itemRow(item) }
                    }
                }
            }
            .frame(maxHeight: 250)
            .background(FN.bg)
            .fnBorder()

            if !request.lockMode {
                FNSegmented(selection: $permanent, items: [(false, "Move to Trash — recoverable"), (true, "Delete permanently")])
            }

            if worst == .danger {
                notice(FN.warn, "Includes items marked DANGER. Removing them can break apps, sync services or macOS features, or delete data that exists nowhere else.")
            }
            if permanent {
                notice(FN.danger, "Permanently deleted items skip the Trash and cannot be recovered by DiskWatch.")
            }
            if request.needsTypedConfirmation {
                HStack(spacing: FN.s2) {
                    Text("Type DELETE to confirm").fnLabel(FN.fg)
                    FNTextField(placeholder: "DELETE", text: $typed, width: 160)
                }
            }
            if permanent {
                Toggle("I understand this cannot be undone", isOn: $acknowledged).toggleStyle(.fnCheckbox)
            }

            HStack(spacing: FN.s2) {
                if allowed.isEmpty { Text("Nothing here can be removed.").fnCaption() }
                Spacer()
                Button("Cancel") { app.deletionRequest = nil }
                    .buttonStyle(.fnSecondary)
                    .keyboardShortcut(.cancelAction)
                Button {
                    Task {
                        working = true
                        var r = request
                        r.permanent = permanent
                        result = await app.perform(r)
                        working = false
                    }
                } label: {
                    Text(working ? "Working…" : (permanent ? "Delete Permanently" : "Move to Trash"))
                }
                .buttonStyle(.fnDanger)
                .keyboardShortcut(.defaultAction)
                .disabled(!canProceed)
            }
        }
    }

    private func itemRow(_ item: DeletionItem) -> some View {
        HStack(alignment: .top, spacing: FN.s2) {
            Image(systemName: item.isDirectory ? "folder" : "doc").font(.system(size: 11)).foregroundStyle(FN.muted).frame(width: 14).padding(.top, 2)
            VStack(alignment: .leading, spacing: 3) {
                Text((item.path as NSString).lastPathComponent).fnData().lineLimit(1)
                Text(Fmt.abbreviate(item.path)).font(FN.mono(11)).foregroundStyle(FN.muted).lineLimit(1).truncationMode(.middle)
                if item.verdict.level != .safe {
                    Text(item.verdict.reasons.joined(separator: " "))
                        .font(FN.mono(11)).foregroundStyle(item.verdict.level == .caution ? FN.muted : item.verdict.level.color)
                        .fixedSize(horizontal: false, vertical: true)
                }
            }
            Spacer()
            VStack(alignment: .trailing, spacing: 4) {
                Text(Fmt.bytes(item.size)).fnData()
                FNPill(text: item.verdict.level.shortTitle, state: item.verdict.level.pill)
            }
        }
        .padding(FN.s2)
        .overlay(alignment: .bottom) { FNRule() }
    }

    private func notice(_ color: Color, _ text: String) -> some View {
        HStack(alignment: .top, spacing: FN.s2) {
            Image(systemName: "exclamationmark.triangle").font(.system(size: 11, weight: .semibold)).foregroundStyle(color)
            Text(text).fnCaption(FN.fg).fixedSize(horizontal: false, vertical: true)
        }
        .padding(FN.s2)
        .frame(maxWidth: .infinity, alignment: .leading)
        .fnBorder(color)
    }

    // MARK: Result

    private func resultView(_ r: DeletionResult) -> some View {
        VStack(alignment: .leading, spacing: FN.s5) {
            FNSectionHeading(title: r.failed.isEmpty ? "Done" : "Finished with problems",
                             caption: "\(r.removed.count) item\(r.removed.count == 1 ? "" : "s") \(permanent ? "deleted" : "moved to Trash")") {
                FNPill(text: r.failed.isEmpty ? "Complete" : "\(r.failed.count) failed", state: r.failed.isEmpty ? .ok : .warn)
            }
            HStack(spacing: FN.s3) {
                FNStatTile(label: permanent ? "Freed" : "Moved to Trash", value: Fmt.bytes(r.freed), valueColor: FN.accent)
                FNStatTile(label: "Removed", value: Fmt.count(r.removed.count))
                FNStatTile(label: "Failed", value: Fmt.count(r.failed.count), valueColor: r.failed.isEmpty ? FN.fg : FN.danger)
            }
            if !permanent && !r.removed.isEmpty {
                Text("Empty the Trash to actually free the space.").fnCaption()
            }
            if !r.failed.isEmpty {
                ScrollView {
                    VStack(alignment: .leading, spacing: 0) {
                        ForEach(Array(r.failed.enumerated()), id: \.offset) { _, f in
                            VStack(alignment: .leading, spacing: 3) {
                                Text(Fmt.abbreviate(f.path)).fnData().lineLimit(1).truncationMode(.middle)
                                Text(f.reason).font(FN.mono(11)).foregroundStyle(FN.danger)
                            }
                            .padding(FN.s2)
                            .frame(maxWidth: .infinity, alignment: .leading)
                            .overlay(alignment: .bottom) { FNRule() }
                        }
                    }
                }
                .frame(maxHeight: 200)
                .fnBorder()
            }
            Text("Every action is recorded in \(Fmt.abbreviate(FileOps.logPath)).").fnCaption()
            HStack {
                Button("Show Log") { RealUser.reveal([FileOps.logPath]) }.buttonStyle(.fnSecondary)
                Spacer()
                Button("Done") { app.deletionRequest = nil }.buttonStyle(.fnPrimary).keyboardShortcut(.defaultAction)
            }
        }
    }
}
