using Application.Models.Tiller;

namespace Application.Interfaces.Services;

// Native Tiller sync: history backfill + ongoing incremental reads through
// the existing read-only client. Manual requests and the optional native
// background worker share this engine; no separate service host.
// Tracer persists its own cache/checkpoints; upstream Tiller is never
// modified, deleted, recategorized, or created by any path here.
public interface ITillerSyncService
{
	Task<TillerSyncResult> SyncFullAsync(CancellationToken cancellationToken);

	Task<TillerSyncResult> SyncIncrementalAsync(CancellationToken cancellationToken);

	Task<TillerSyncStatus> GetSyncStatusAsync(CancellationToken cancellationToken);
}
