// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.Updater.Domain;

public record UpdateExecutionResult(UpdateExecutionStatus Status, string? BackupRef, string Message);
