// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.Updater.Domain;

public record UpdateExecutionResult(UpdateExecutionStatus Status, string? BackupRef, string Message);
