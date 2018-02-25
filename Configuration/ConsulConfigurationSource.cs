﻿using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace ConsulRx.Configuration
{
    public class ConsulConfigurationSource : IConfigurationSource
    {
        private readonly ObservableConsulConfiguration _consulConfig = new ObservableConsulConfiguration();
        private readonly ConsulDependencies _consulDependencies = new ConsulDependencies();
        private readonly ServiceConfigMappingCollection _serviceConfigMappings = new ServiceConfigMappingCollection();
        private readonly KVTreeConfigMappingCollection _kvTreeConfigMappings = new KVTreeConfigMappingCollection();
        private readonly KVItemConfigMappingCollection _kvItemConfigMappings = new KVItemConfigMappingCollection();
        private IEmergencyCache _cache = new FileSystemEmergencyCache();
        private bool _autoUpdate;

        public ConsulConfigurationSource()
        {
            var autoUpdateEnv = Environment.GetEnvironmentVariable("CONSULRX_AUTO_UPDATE");
            if(autoUpdateEnv != null &&
               (autoUpdateEnv.Equals("1") || autoUpdateEnv.Equals("true", StringComparison.OrdinalIgnoreCase)))
            {
                AutoUpdate();
            }
        }
        
        public ConsulConfigurationSource Endpoint(string consulEndpoint)
        {
            _consulConfig.Endpoint = consulEndpoint;

            return this;
        }
        public ConsulConfigurationSource Endpoint(Predicate<ConsulConfigurationSource> condition, string consulEndpoint)
        {
            return !condition(this) ? this : Endpoint(consulEndpoint);
        }

        public ConsulConfigurationSource Endpoint(string consulEndpoint, string aclToken)
        {
            _consulConfig.Endpoint = consulEndpoint;
            _consulConfig.AclToken = aclToken;

            return this;
        }

        public ConsulConfigurationSource Endpoint(Predicate<ConsulConfigurationSource> condition, string consulEndpoint, string aclToken)
        {
            return !condition(this) ? this : Endpoint(consulEndpoint,aclToken);
        }

        public ConsulConfigurationSource MapService(string consulServiceName, string configKey,
            Func<ServiceNode, string> endpointFormatter, Func<ServiceNode[], ServiceNode> nodeSelector)
        {
            _consulDependencies.Services.Add(consulServiceName);
            _serviceConfigMappings.Add(new SingleNodeServiceConfigMapping(configKey, consulServiceName, endpointFormatter, nodeSelector));
            
            return this;
        }

        public ConsulConfigurationSource MapService(Predicate<ConsulConfigurationSource> condition, string consulServiceName, string configKey,
            Func<ServiceNode, string> endpointFormatter, Func<ServiceNode[], ServiceNode> nodeSelector)
        {
            return !condition(this) ? this : MapService(consulServiceName,configKey,endpointFormatter,nodeSelector);
        }

        public ConsulConfigurationSource MapService(string consulServiceName, string configKey,
            Func<ServiceNode, string> endpointFormatter, Func<ServiceNode[], IEnumerable<ServiceNode>> nodeSelector)
        {
            _consulDependencies.Services.Add(consulServiceName);
            _serviceConfigMappings.Add(new MultipleNodeServiceConfigMapping(configKey, consulServiceName, endpointFormatter, nodeSelector));
            
            return this;
        }
        public ConsulConfigurationSource MapService(Predicate<ConsulConfigurationSource> condition, string consulServiceName, string configKey,
            Func<ServiceNode, string> endpointFormatter, Func<ServiceNode[], IEnumerable<ServiceNode>> nodeSelector)
        {

            return !condition(this) ? this : MapService(consulServiceName, configKey, endpointFormatter, nodeSelector);
        }

        public ConsulConfigurationSource MapHttpService(string consulServiceName, string configKey)
        {
            return MapService(consulServiceName, configKey, EndpointFormatters.Http, NodeSelectors.First);
        }
        public ConsulConfigurationSource MapHttpService(Predicate<ConsulConfigurationSource> condition, string consulServiceName, string configKey)
        {
            return !condition(this) ? this : MapHttpService(consulServiceName, configKey);
        }

        public ConsulConfigurationSource MapHttpsService(string consulServiceName, string configKey)
        {
            return MapService(consulServiceName, configKey, EndpointFormatters.Https, NodeSelectors.First);
        }
        public ConsulConfigurationSource MapHttpsService(Predicate<ConsulConfigurationSource> condition, string consulServiceName, string configKey)
        {
            return !condition(this) ? this : MapHttpsService(consulServiceName, configKey);
        }

        public ConsulConfigurationSource MapKeyPrefix(string consulKeyPrefix, string configKey)
        {
            _consulDependencies.KeyPrefixes.Add(consulKeyPrefix);
            _kvTreeConfigMappings.Add(new KVTreeConfigMapping(configKey, consulKeyPrefix));

            return this;
        }

        public ConsulConfigurationSource MapKeyPrefix(Predicate<ConsulConfigurationSource> condition, string consulKeyPrefix, string configKey)
        {
            return !condition(this) ? this : MapKeyPrefix(consulKeyPrefix, configKey);

        }

        public ConsulConfigurationSource MapKey(string consulKey, string configKey)
        {
            _consulDependencies.Keys.Add(consulKey);
            _kvItemConfigMappings.Add(new KVItemConfigMapping(configKey, consulKey));

            return this;
        }
        public ConsulConfigurationSource MapKey(Predicate<ConsulConfigurationSource> condition, string consulKey, string configKey)
        {
            return !condition(this) ? this : MapKey(consulKey, configKey);
        }

        /// <summary>
        /// Configures a periodic, automatic update based on
        /// <paramref name="options"/>.
        /// </summary>
        /// <param name="options">
        /// The settings for automatic updates.
        /// </param>
        /// <returns>
        /// The same instance on which the method was called.
        /// </returns>
        public ConsulConfigurationSource AutoUpdate(AutoUpdateOptions options = null)
        {
            _autoUpdate = true;
            options = options ?? new AutoUpdateOptions();
            
            _consulConfig.RetryDelay = options.ErrorRetryInterval;
            _consulConfig.LongPollMaxWait = options.UpdateMaxInterval;

            return this;
        }

        IConfigurationProvider IConfigurationSource.Build(IConfigurationBuilder builder)
        {
            var consulClient = new ObservableConsul(_consulConfig);

            return Build(consulClient);
        }

        internal IConfigurationProvider Build(IObservableConsul consulClient)
        {
            return new ConsulConfigurationProvider(consulClient, _cache, _consulDependencies, _serviceConfigMappings, _kvTreeConfigMappings, _kvItemConfigMappings, _autoUpdate);
        }

        internal ConsulConfigurationSource UseCache(IEmergencyCache cache)
        {
            _cache = cache;

            return this;
        }
    }

    public static class ConsulConfigurationExtensions
    {
        public static IConfigurationBuilder AddConsul(this IConfigurationBuilder builder, Action<ConsulConfigurationSource> configureSource)
        {
            var source = new ConsulConfigurationSource();
            configureSource(source);
            builder.Add(source);

            return builder;
        }
    }
}