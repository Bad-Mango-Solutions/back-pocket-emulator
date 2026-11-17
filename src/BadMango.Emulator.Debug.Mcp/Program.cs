// <copyright file="Program.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

using BadMango.Emulator.Debug.Mcp;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

using Serilog;

var builder = DebugMcpHost.Create(args);

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly(typeof(DebugMcpHost).Assembly, DebugMcpJson.SerializerOptions);

try
{
    using var host = builder.Build();
    await host.RunAsync();
    return 0;
}
catch (OptionsValidationException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}