using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Spiffy.Monitoring;

namespace ConsulRx.Configuration
{
    public class ConsulConfigurationProvider : ConfigurationProvider
    {
        private readonly IObservableConsul _consulClient;
        private readonly IEmergencyCache _emergencyCache;
        private readonly ConsulDependencies _dependencies;
        private readonly ServiceConfigMappingCollection _serviceConfigMappings;
        private readonly KVTreeConfigMappingCollection _kvTreeConfigMappings;
        private readonly KVItemConfigMappingCollection _kvItemConfigMappings;
        private readonly KVItemConfigMappingCollection _kvJsonConfigMappings;
        private readonly bool _autoUpdate;
        private ConsulState _consulState;
        private readonly JsonNodeParser _nodeParser = new JsonNodeParser();

        public ConsulConfigurationProvider(IObservableConsul consulClient,
            IEmergencyCache emergencyCache, 
            ConsulDependencies dependencies,
            ServiceConfigMappingCollection serviceConfigMappings,
            KVTreeConfigMappingCollection kvTreeConfigMappings,
            KVItemConfigMappingCollection kvItemConfigMappings,
            KVItemConfigMappingCollection kvJsonConfigMappings,
            bool autoUpdate)
        {
            _consulClient = consulClient;
            _emergencyCache = emergencyCache;
            _dependencies = dependencies;
            _serviceConfigMappings = serviceConfigMappings;
            _kvTreeConfigMappings = kvTreeConfigMappings;
            _kvItemConfigMappings = kvItemConfigMappings;
            _kvJsonConfigMappings = kvJsonConfigMappings;
            _autoUpdate = autoUpdate;
        }

        public override void Load()
        {
            LoadAsync().GetAwaiter().GetResult();
        }

        public async Task LoadAsync()
        {
            var eventContext = new EventContext("ConsulRx.Configuration", "Load");
            try
            {
                _consulState = await _consulClient.GetDependenciesAsync(_dependencies);
                UpdateData();
                eventContext["LoadedFrom"] = "Consul";
            }
            catch (Exception exception)
            {
                eventContext.IncludeException(exception);
                if (_emergencyCache.TryLoad(out var cachedData))
                {
                    eventContext["LoadedFrom"] = "EmergencyCache";
                    Data = cachedData;
                }
                else
                {
                    eventContext["LoadedFrom"] = "UnableToLoad";
                    throw new ConsulRxConfigurationException("Unable to load configuration from consul. It is likely down or the endpoint is misconfigured. Please check the InnerException for details.", exception);
                }
            }
            finally
            {
                eventContext.Dispose();
            }

            if (_autoUpdate)
            {
                _consulClient.ObserveDependencies(_dependencies).DelayedRetry(_consulClient.Configuration.RetryDelay ?? TimeSpan.Zero).Subscribe(updatedState =>
                {
                    using (var reloadEventContext = new EventContext("ConsulRx.Configuration", "Reload"))
                    {
                        try
                        {
                            _consulState = updatedState;
                            UpdateData();
                            OnReload();
                        }
                        catch (Exception ex)
                        {
                            reloadEventContext.IncludeException(ex);
                        }
                    }
                });
            }
        }

        private void UpdateData()
        {
            var data = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            AddServiceData(data);
            AddKVTreeData(data);
            AddKVItemData(data);
            AddKVJsonData(data);

            Data = data;
            _emergencyCache.Save(data);
        }

        private void AddKVJsonData(Dictionary<string, string> data)
        {
            foreach (var mapping in _kvJsonConfigMappings)
            {
                var value = _consulState.KVStore.GetValue(mapping.ConsulKey);
                if (value != null)
                {
                    foreach (var keyValuePair in _nodeParser.Parse(value))
                    {
                        data[$"{mapping.ConfigKey}:{keyValuePair.Key}"] = keyValuePair.Value;
                    }
                }
            }
        }

        private void AddKVItemData(Dictionary<string, string> data)
        {
            foreach (var mapping in _kvItemConfigMappings)
            {
                var value = _consulState.KVStore.GetValue(mapping.ConsulKey);
                data[mapping.ConfigKey] = value;
            }
        }

        private void AddServiceData(Dictionary<string, string> data)
        {
            foreach (var mapping in _serviceConfigMappings)
            {
                var service = _consulState.GetService(mapping.ServiceName);
                if(service != null)
                {
                    mapping.BindToConfiguration(service, data);
                }
            }
        }

        private void AddKVTreeData(Dictionary<string, string> data)
        {
            foreach (var mapping in _kvTreeConfigMappings)
            {
                foreach (var kv in _consulState.KVStore.GetTree(mapping.ConsulKeyPrefix))
                {
                    var fullConfigKey = mapping.FullConfigKey(kv);
                    data[fullConfigKey] = kv.Value;
                }
            }
        }
    }
}