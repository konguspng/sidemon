using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Threading;
using System.Windows.Media;
using LibreHardwareMonitor.Hardware;
using SidebarDiagnostics.Framework;

namespace SidebarDiagnostics.Monitoring
{
    // Network throughput is measured from NetworkInterface byte counters sampled each
    // poll; no perf counters, no name-mangling between counter instances and adapters.
    public class NetworkMonitor : BaseMonitor
    {
        public NetworkMonitor(NetworkInterface nic, string name, string extIP, MetricConfig[] metrics, bool showName = true, bool roundAll = false, bool useBytes = false, double bandwidthInAlert = 0, double bandwidthOutAlert = 0) : base(nic.Id, name, showName)
        {
            _nic = nic;

            iConverter _converter;

            if (useBytes)
            {
                _converter = BytesPerSecondConverter.Instance;
            }
            else
            {
                _converter = BitsPerSecondConverter.Instance;
            }

            List<iMetric> _metrics = new List<iMetric>();

            if (metrics.IsEnabled(MetricKey.NetworkIP))
            {
                string _ipAddress = GetAdapterIPAddress(nic);

                if (!string.IsNullOrEmpty(_ipAddress))
                {
                    _metrics.Add(new IPMetric(_ipAddress, MetricKey.NetworkIP, DataType.IP));
                }
            }

            if (!string.IsNullOrEmpty(extIP))
            {
                _metrics.Add(new IPMetric(extIP, MetricKey.NetworkExtIP, DataType.IP));
            }

            if (metrics.IsEnabled(MetricKey.NetworkIn))
            {
                InMetric = new BaseMetric(MetricKey.NetworkIn, DataType.kbps, null, roundAll, bandwidthInAlert, _converter);
                _metrics.Add(InMetric);
            }

            if (metrics.IsEnabled(MetricKey.NetworkOut))
            {
                OutMetric = new BaseMetric(MetricKey.NetworkOut, DataType.kbps, null, roundAll, bandwidthOutAlert, _converter);
                _metrics.Add(OutMetric);
            }

            Metrics = _metrics.ToArray();
        }

        ~NetworkMonitor()
        {
            Dispose(false);
        }

        public static IEnumerable<HardwareConfig> GetHardware()
        {
            return GetAdapters()
                .OrderBy(n => n.Name)
                .Select(n => new HardwareConfig() { ID = n.Id, Name = n.Name, ActualName = n.Name, Enabled = false });
        }

        private static IEnumerable<NetworkInterface> GetAdapters()
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(n =>
                    n.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                    n.NetworkInterfaceType != NetworkInterfaceType.Tunnel &&
                    n.OperationalStatus == OperationalStatus.Up);
        }

        public static iMonitor[] GetInstances(HardwareConfig[] hardwareConfig, MetricConfig[] metrics, ConfigParam[] parameters)
        {
            bool _showName = parameters.GetValue<bool>(ParamKey.HardwareNames);
            bool _roundAll = parameters.GetValue<bool>(ParamKey.RoundAll);
            bool _useBytes = parameters.GetValue<bool>(ParamKey.UseBytes);
            int _bandwidthInAlert = parameters.GetValue<int>(ParamKey.BandwidthInAlert);
            int _bandwidthOutAlert = parameters.GetValue<int>(ParamKey.BandwidthOutAlert);

            string _extIP = null;

            if (metrics.IsEnabled(MetricKey.NetworkExtIP))
            {
                _extIP = GetExternalIPAddress();
            }

            return (
                from nic in GetAdapters()
                join c in hardwareConfig on nic.Id equals c.ID into merged
                from n in merged.DefaultIfEmpty(new HardwareConfig() { ID = nic.Id, Name = nic.Name, ActualName = nic.Name, Enabled = false }).Select(n => { n.ActualName = nic.Name; return n; })
                where n.Enabled
                orderby n.Order descending, n.Name ascending
                select new NetworkMonitor(nic, n.Name ?? n.ActualName, _extIP, metrics, _showName, _roundAll, _useBytes, _bandwidthInAlert, _bandwidthOutAlert)
                ).ToArray();
        }

        public override void Update()
        {
            if (InMetric == null && OutMetric == null)
            {
                base.Update();
                return;
            }

            try
            {
                IPInterfaceStatistics _stats = _nic.GetIPStatistics();

                long _now = Stopwatch.GetTimestamp();

                if (_lastTimestamp > 0)
                {
                    double _seconds = (_now - _lastTimestamp) / (double)Stopwatch.Frequency;

                    if (_seconds > 0d)
                    {
                        if (InMetric != null)
                        {
                            double _bytesIn = Math.Max(0, _stats.BytesReceived - _lastBytesReceived);
                            InMetric.Update(_bytesIn / _seconds);
                        }

                        if (OutMetric != null)
                        {
                            double _bytesOut = Math.Max(0, _stats.BytesSent - _lastBytesSent);
                            OutMetric.Update(_bytesOut / _seconds);
                        }
                    }
                }

                _lastBytesReceived = _stats.BytesReceived;
                _lastBytesSent = _stats.BytesSent;
                _lastTimestamp = _now;
            }
            catch (NetworkInformationException)
            {
                return;
            }

            base.Update();
        }

        private static string GetAdapterIPAddress(NetworkInterface nic)
        {
            foreach (IPAddressInformation unicast in nic.GetIPProperties().UnicastAddresses)
            {
                if (unicast.Address.AddressFamily == AddressFamily.InterNetwork)
                {
                    return unicast.Address.ToString();
                }
            }

            return null;
        }

        private static string GetExternalIPAddress()
        {
            try
            {
                using (HttpClient _client = new HttpClient() { Timeout = TimeSpan.FromSeconds(5) })
                {
                    return _client.GetStringAsync(Constants.URLs.IPIFY).GetAwaiter().GetResult();
                }
            }
            catch (Exception)
            {
                return "";
            }
        }

        public iMetric InMetric { get; private set; }

        public iMetric OutMetric { get; private set; }

        private NetworkInterface _nic { get; set; }

        private long _lastBytesReceived { get; set; }

        private long _lastBytesSent { get; set; }

        private long _lastTimestamp { get; set; }
    }
}
