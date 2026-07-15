using DropsCleaner.Commands;
using DropsCleaner.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Spectre.Console.Cli;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSingleton<DropScanner>();

var registrar = new CustomTypeRegistrar(builder.Services);

var app = new CommandApp(registrar);
app.Configure(config =>
{
    config.AddCommand<AnalyzeCommand>("analyze");
});

return await app.RunAsync(args);