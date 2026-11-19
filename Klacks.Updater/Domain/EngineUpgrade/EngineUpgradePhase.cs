// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.Updater.Domain.EngineUpgrade;

public enum EngineUpgradePhase
{
    Accepted = 0,
    ArtifactReady = 1,
    ApplicationStopped = 2,
    SnapshotTaken = 3,
    DumpCreated = 4,
    OldEngineStopped = 5,
    NewEngineActive = 6,
    Restored = 7,
    Verified = 8,
    ConfigurationCommitted = 9,
    ApplicationHealthy = 10,
    Committed = 11,
    RolledBack = 12,
    RollbackFailed = 13,
}
