// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Windows service lookups shared by the native STT adapters.
/// </summary>

using System.Runtime.Versioning;
using System.ServiceProcess;

namespace Klacks.Updater.Infrastructure;

[SupportedOSPlatform("windows")]
public static class WindowsServices
{
    public static bool Exists(string serviceName)
    {
        var services = ServiceController.GetServices();
        try
        {
            return services.Any(service => string.Equals(service.ServiceName, serviceName, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            foreach (var service in services)
            {
                service.Dispose();
            }
        }
    }
}
