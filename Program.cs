using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace filewatcher
{
    public class Program
    {
        public static void Main(string[] args)
        {
            try {
                CreateHostBuilder(args).Build().Run();
            } catch (OperationCanceledException) {}
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureServices((hostContext, services) =>
                {
                    IConfiguration configuration = hostContext.Configuration;
                    WorkerConfig options = configuration.GetSection("Daemon").Get<WorkerConfig>();

                    services.AddSingleton(options);
                    services.AddHostedService<Worker>();

                });
    }
}
