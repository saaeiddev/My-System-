using System.Globalization;
using System.Management;
using System.Net.NetworkInformation;
using MySystem.Core;

namespace MySystem.Hardware;

public sealed class HardwareInventory
{
    public IReadOnlyList<InventoryItem> Collect(CancellationToken cancellationToken = default)
    {
        var items = new List<InventoryItem>();
        Query(items, "CPU", "Win32_Processor", ["Name", "Manufacturer", "NumberOfCores", "NumberOfLogicalProcessors", "MaxClockSpeed"], cancellationToken);
        Query(items, "GPU", "Win32_VideoController", ["Name", "AdapterCompatibility", "DriverVersion", "VideoProcessor",
            "CurrentHorizontalResolution", "CurrentVerticalResolution", "CurrentRefreshRate"], cancellationToken);
        // Win32_VideoController.AdapterRAM is a 32-bit property and is deliberately not used as VRAM capacity.
        Query(items, "Memory module", "Win32_PhysicalMemory", ["DeviceLocator", "Capacity", "ConfiguredClockSpeed", "Speed", "Manufacturer", "PartNumber"], cancellationToken);
        Query(items, "Motherboard", "Win32_BaseBoard", ["Manufacturer", "Product", "Version"], cancellationToken);
        Query(items, "BIOS", "Win32_BIOS", ["Manufacturer", "SMBIOSBIOSVersion", "ReleaseDate"], cancellationToken);
        Query(items, "System", "Win32_ComputerSystem", ["Manufacturer", "Model", "TotalPhysicalMemory", "SystemType"], cancellationToken);
        Query(items, "Windows", "Win32_OperatingSystem", ["Caption", "Version", "BuildNumber", "OSArchitecture"], cancellationToken);
        Query(items, "Storage device", "Win32_DiskDrive", ["Model", "Size", "InterfaceType", "MediaType", "FirmwareRevision"], cancellationToken);
        Query(items, "Storage bus", "MSFT_PhysicalDisk", ["FriendlyName", "MediaType", "BusType", "Size"], cancellationToken,
            @"root\Microsoft\Windows\Storage");
        Query(items, "Battery", "Win32_Battery", ["Name", "BatteryStatus", "EstimatedChargeRemaining", "EstimatedRunTime"], cancellationToken);
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            cancellationToken.ThrowIfCancellationRequested();
            items.Add(new("Network", adapter.Name, adapter.Description));
            items.Add(new("Network", adapter.Name + " status", adapter.OperationalStatus.ToString()));
            try
            {
                items.Add(new("Network", adapter.Name + " link speed", adapter.Speed > 0 ? adapter.Speed.ToString(CultureInfo.InvariantCulture) + " bit/s" : "Unavailable"));
                items.Add(new("Network", adapter.Name + " addresses", string.Join(", ", adapter.GetIPProperties().UnicastAddresses.Select(a => a.Address.ToString()))));
            }
            catch (Exception error) { items.Add(new("Network", adapter.Name + " details", "Unavailable: " + error.Message)); }
        }
        foreach (var volume in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (!volume.IsReady) continue;
                items.Add(new("Volume", volume.Name, $"{volume.VolumeLabel} • {volume.DriveFormat} • {volume.TotalSize:N0} bytes capacity • {volume.TotalFreeSpace:N0} bytes free"));
            }
            catch (Exception error) { items.Add(new("Volume", volume.Name, "Unavailable: " + error.Message)); }
        }
        return items;
    }

    private static void Query(List<InventoryItem> items, string category, string className, string[] properties,
        CancellationToken cancellationToken, string scope = @"root\cimv2")
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var searcher = new ManagementObjectSearcher(new ManagementScope(scope),
                new ObjectQuery($"SELECT {string.Join(',', properties)} FROM {className}"),
                new System.Management.EnumerationOptions { Timeout = TimeSpan.FromSeconds(3), ReturnImmediately = false });
            using var results = searcher.Get();
            int index = 0;
            foreach (ManagementObject item in results)
            {
                using (item)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    index++;
                    foreach (var property in properties)
                    {
                        object? raw = item[property];
                        string value = Convert.ToString(raw, CultureInfo.InvariantCulture)?.Trim() ?? "";
                        if (className == "MSFT_PhysicalDisk" && uint.TryParse(value, out uint number))
                            value = property switch
                            {
                                "MediaType" => number switch { 3 => "HDD", 4 => "SSD", 5 => "Storage-class memory", _ => "Unspecified" },
                                "BusType" => number switch { 7 => "USB", 8 => "RAID", 11 => "SATA", 17 => "NVMe", 10 => "SAS", 15 => "File-backed virtual", _ => "Windows bus type " + number },
                                _ => value
                            };
                        if (property == "EstimatedRunTime" && value == "71582788") value = "Unavailable (AC power or no estimate)";
                        items.Add(new(category + " " + index, property, string.IsNullOrWhiteSpace(value) ? "Unavailable" : value));
                    }
                }
            }
            if (index == 0) items.Add(new(category, "Availability", "No supported device reported by " + className));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception error) { items.Add(new(category, "Availability", "Unavailable: " + error.Message)); }
    }
}
