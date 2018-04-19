// Copyright (c) Microsoft. All rights reserved.

using System.Reflection;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Microsoft.Azure.IoTSolutions.DeviceSimulation.Services;
using Microsoft.Azure.IoTSolutions.DeviceSimulation.Services.Concurrency;
using Microsoft.Azure.IoTSolutions.DeviceSimulation.Services.Diagnostics;
using Microsoft.Azure.IoTSolutions.DeviceSimulation.Services.Runtime;
using Microsoft.Azure.IoTSolutions.DeviceSimulation.SimulationAgent;
using Microsoft.Azure.IoTSolutions.DeviceSimulation.SimulationAgent.DeviceConnection;
using Microsoft.Azure.IoTSolutions.DeviceSimulation.SimulationAgent.DeviceProperties;
using Microsoft.Azure.IoTSolutions.DeviceSimulation.SimulationAgent.DeviceState;
using Microsoft.Azure.IoTSolutions.DeviceSimulation.SimulationAgent.DeviceTelemetry;
using Microsoft.Azure.IoTSolutions.DeviceSimulation.WebService.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Azure.IoTSolutions.DeviceSimulation.WebService
{
    public static class DependencyResolution
    {
        /// <summary>
        /// Autofac configuration. Find more information here:
        /// @see http://docs.autofac.org/en/latest/integration/aspnetcore.html
        /// </summary>
        public static IContainer Setup(IServiceCollection services)
        {
            var builder = new ContainerBuilder();

            builder.Populate(services);

            AutowireAssemblies(builder);
            SetupCustomRules(builder);

            var container = builder.Build();
            RegisterFactory(container);

            return container;
        }

        /// <summary>
        /// Autowire interfaces to classes from all the assemblies, to avoid
        /// manual configuration. Note that autowiring works only for interfaces
        /// with just one implementation.
        /// @see http://autofac.readthedocs.io/en/latest/register/scanning.html
        /// </summary>
        private static void AutowireAssemblies(ContainerBuilder builder)
        {
            var assembly = Assembly.GetEntryAssembly();
            builder.RegisterAssemblyTypes(assembly).AsImplementedInterfaces();

            // Auto-wire Services.DLL
            assembly = typeof(IServicesConfig).GetTypeInfo().Assembly;
            builder.RegisterAssemblyTypes(assembly).AsImplementedInterfaces();

            // Auto-wire SimulationAgent.DLL
            assembly = typeof(ISimulation).GetTypeInfo().Assembly;
            builder.RegisterAssemblyTypes(assembly).AsImplementedInterfaces();
        }

        /// <summary>
        /// Setup custom rules overriding autowired ones, for example in cases
        /// where an interface has multiple implementations, and cases where
        /// a singleton is preferred to new instances.
        /// </summary>
        private static void SetupCustomRules(ContainerBuilder builder)
        {
            // Make sure the configuration is read only once.
            IConfig config = new Config(new ConfigData(new Logger(Uptime.ProcessId)));
            builder.RegisterInstance(config).As<IConfig>().SingleInstance();

            // Service configuration is generated by the entry point, so we
            // prepare the instance here.
            builder.RegisterInstance(config.LoggingConfig).As<ILoggingConfig>().SingleInstance();
            builder.RegisterInstance(config.ServicesConfig).As<IServicesConfig>().SingleInstance();
            builder.RegisterInstance(config.RateLimitingConfig).As<IRateLimitingConfig>().SingleInstance();
            builder.RegisterInstance(config.DeploymentConfig).As<IDeploymentConfig>().SingleInstance();

            // Instantiate only one logger
            var logger = new Logger(Uptime.ProcessId, config.LoggingConfig);
            builder.RegisterInstance(logger).As<ILogger>().SingleInstance();

            // Auth and CORS setup
            Auth.Startup.SetupDependencies(builder, config);

            // By default the DI container create new objects when injecting
            // dependencies. To improve performance we reuse some instances,
            // for example to reuse IoT Hub connections, as opposed to creating
            // a new connection every time.
            builder.RegisterType<Simulations>().As<ISimulations>().SingleInstance();
            builder.RegisterType<DeviceModels>().As<IDeviceModels>().SingleInstance();
            builder.RegisterType<Services.Devices>().As<IDevices>().SingleInstance();
            builder.RegisterType<RateLimiting>().As<IRateLimiting>().SingleInstance();

            // The simulation runner contains the service counters, which are read and
            // written by multiple parts of the application, so we need to make sure 
            // there is only one instance storing that information.
            builder.RegisterType<SimulationRunner>().As<ISimulationRunner>().SingleInstance();

            // Registrations required by Autofac, these classes implement the same interface
            builder.RegisterType<Connect>().As<Connect>();
            builder.RegisterType<SetDeviceTag>().As<SetDeviceTag>();
            builder.RegisterType<Fetch>().As<Fetch>();
            builder.RegisterType<Register>().As<Register>();
            builder.RegisterType<UpdateDeviceState>().As<UpdateDeviceState>();
            builder.RegisterType<SendTelemetry>().As<SendTelemetry>();
            builder.RegisterType<UpdateReportedProperties>().As<UpdateReportedProperties>();
        }

        private static void RegisterFactory(IContainer container)
        {
            Factory.RegisterResolver(container.Resolve);
        }
    }
}
