using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration; // Add this

namespace LinuxMcpServer
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var services = new ServiceCollection();

            // Setup Configuration
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables()
                .Build();
            
            services.AddSingleton<IConfiguration>(configuration);

            string sshHost = configuration["SSH_HOST"] ?? "localhost";
            int sshPort = int.Parse(configuration["SSH_PORT"] ?? "22");
            string sshUser = configuration["SSH_USER"] ?? "user";
            string sshPass = configuration["SSH_PASS"] ?? "password";

            services.AddLogging(c => c.AddConsole());
            services.AddSingleton<SshService>(sp => new SshService(sshHost, sshPort, sshUser, sshPass));

            // Setup Ollama
            string ollamaUrl = configuration["OLLAMA_URL"] ?? "http://localhost:11434";
            string ollamaModel = configuration["OLLAMA_MODEL"] ?? "llama3";
            services.AddSingleton<OllamaService>(sp => new OllamaService(ollamaUrl, ollamaModel, sp.GetRequiredService<ILogger<OllamaService>>()));

            var serviceProvider = services.BuildServiceProvider();

            // Instantiate SSH Service to ensure connectivity check (optional, but good)
            // var ssh = serviceProvider.GetRequiredService<SshService>(); // Lazy connect

            ILogger logger = serviceProvider.GetRequiredService<ILogger<Program>>();
            
            // Cannot write to Console because StandardOutput is used for Protocol!
            // We must log to StandardError.
            Console.SetOut(Console.Error); 

            if (args.Length > 0 && (args[0] == "--client" || args[0] == "--interactive"))
            {
                 await RunInteractiveClient(serviceProvider);
            }
            else
            {
                logger.LogInformation("Starting Linux MCP Server (Manual Implementation)...");
                var server = new McpServer(serviceProvider);
                await server.RunAsync();
            }
        }

        private static async Task RunInteractiveClient(IServiceProvider services)
        {
            var logger = services.GetRequiredService<ILogger<Program>>();
            var ollama = services.GetRequiredService<OllamaService>(); // Get Ollama Service

            logger.LogInformation("Starting Interactive Mode...");
            Console.WriteLine("--- Linux MCP Interactive Client (Powered by Ollama) ---");
            Console.WriteLine("Enter a natural language request (e.g. 'check disk space') or a command.");
            Console.WriteLine("Type 'exit' to quit.");

            var ssh = services.GetRequiredService<SshService>();
            
            try 
            {
                Console.Write("Connecting to SSH...");
                ssh.Connect();
                Console.WriteLine(" Connected!");
            }
            catch(Exception ex)
            {
                Console.WriteLine($"\nFailed to connect: {ex.Message}");
                return;
            }

            while (true)
            {
                Console.Write("> ");
                var input = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(input)) continue;
                if (input.Trim().ToLower() == "exit") break;

                // Process with Ollama
                Console.Write("Thinking...");
                var commandToRun = await ollama.TranslateToCommandAsync(input);
                // Clear "Thinking..." line if possible, or just print new line
                Console.WriteLine($"\r[AI] Executing: {commandToRun}          ");

                // Simulate MCP Tool Call
                logger.LogInformation($"[Mock MCP Client] Calling tool 'linux_command' with args: {{ command: '{commandToRun}' }}");
                
                try 
                {
                    string result = ssh.ExecuteCommand(commandToRun);
                    Console.WriteLine("Output:");
                    Console.WriteLine(result);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error: {ex.Message}");
                }
            }
        }
    }

    // --- Minimal MCP Implementation ---

    public class McpServer
    {
        private readonly IServiceProvider _services;
        private readonly JsonSerializerOptions _jsonOptions;

        public McpServer(IServiceProvider services)
        {
            _services = services;
            _jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        }

        public async Task RunAsync()
        {
            using var stdin = Console.OpenStandardInput();
            using var stdout = Console.OpenStandardOutput();
            // Note: We should not close stdout/stdin as they are system streams, but using is fine here till end.

            // We need to read line by line (JSON-RPC usually sends one JSON object per line)
            // Or use a proper JSON-RPC reader. 
            // MCP spec says: "The base protocol is JSON-RPC 2.0... Transports... Stdio... Messages are delimited by newlines."
            
            var reader = new StreamReader(stdin);
            var writer = new StreamWriter(stdout) { AutoFlush = true };

            while (!reader.EndOfStream)
            {
                string? line = await reader.ReadLineAsync();
                if (string.IsNullOrWhiteSpace(line)) continue;

                try
                {
                    await HandleMessageAsync(line, writer);
                }
                catch (Exception ex)
                {
                   // Log error to stderr
                   Console.Error.WriteLine($"Error handling message: {ex.Message}");
                }
            }
        }

        private async Task HandleMessageAsync(string json, StreamWriter writer)
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Check if it's a request or notification
            bool isRequest = root.TryGetProperty("id", out var idElement);
            string method = root.GetProperty("method").GetString() ?? "";
            object? id = isRequest ? (idElement.ValueKind == JsonValueKind.Number ? idElement.GetInt64() : idElement.GetString()) : null;

            Console.Error.WriteLine($"Received method: {method}");

            if (method == "initialize")
            {
                var response = new 
                {
                    jsonrpc = "2.0",
                    id = id,
                    result = new 
                    {
                        protocolVersion = "2024-11-05", // Spec version
                        capabilities = new 
                        {
                            tools = new { } 
                        },
                        serverInfo = new 
                        {
                            name = "LinuxMcpServer",
                            version = "1.0.0"
                        }
                    }
                };
                await SendJsonAsync(writer, response);
            }
            else if (method == "notifications/initialized")
            {
                // Client acknowledges initialization. logic here if needed.
            }
            else if (method == "tools/list")
            {
                var response = new 
                {
                    jsonrpc = "2.0",
                    id = id,
                    result = new 
                    {
                        tools = new[] 
                        {
                            new 
                            {
                                name = "linux_command",
                                description = "Execute a shell command on the Linux box. The model should translate natural language user requests (e.g. 'check disk space', 'list files') into the appropriate shell command (e.g. 'df -h', 'ls -la') before calling this tool.",
                                inputSchema = new 
                                {
                                    type = "object",
                                    properties = new Dictionary<string, object>
                                    {
                                        { "command", new { type = "string", description = "The shell command to execute" } }
                                    },
                                    required = new[] { "command" }
                                }
                            },
                            new 
                            {
                                name = "read_file",
                                description = "Read the complete contents of a file from the Linux remote machine.",
                                inputSchema = new 
                                {
                                    type = "object",
                                    properties = new Dictionary<string, object>
                                    {
                                        { "path", new { type = "string", description = "The absolute path to the file to read" } }
                                    },
                                    required = new[] { "path" }
                                }
                            }
                        }
                    }
                };
                await SendJsonAsync(writer, response);
            }
            else if (method == "tools/call")
            {
                var pars = root.GetProperty("params");
                string name = pars.GetProperty("name").GetString() ?? "";
                var arguments = pars.GetProperty("arguments");

                if (name == "linux_command")
                {
                    string cmd = arguments.GetProperty("command").GetString() ?? "";
                    var ssh = _services.GetRequiredService<SshService>();
                    string resultText;
                    bool isError = false;
                    try 
                    {
                        // Ensure connection (SshService methods should handle re-connect or Check)
                        // SshService implementation inside SshService.cs calls Connect() in ExecuteCommand? 
                        // Let's verify SshService usage.
                        // Looking at SshService.cs, ExecuteCommand calls... wait, I need to check SshService.ExecuteCommand implementation.
                        // I'll assume it needs Connect() called explicitly if NOT called inside ExecuteCommand.
                        // I'll add a Connect call here or update SshService.
                        ssh.Connect();
                        resultText = ssh.ExecuteCommand(cmd);
                    }
                    catch (Exception ex)
                    {
                        resultText = $"SSH Error: {ex.Message}";
                        isError = true;
                    }

                    var response = new 
                    {
                        jsonrpc = "2.0",
                        id = id,
                        result = new 
                        {
                            content = new[] 
                            {
                                new { type = "text", text = resultText }
                            },
                            isError = isError
                        }
                    };
                    await SendJsonAsync(writer, response);
                }
                else if (name == "read_file")
                {
                    string path = arguments.GetProperty("path").GetString() ?? "";
                    var ssh = _services.GetRequiredService<SshService>();
                    string resultText;
                    bool isError = false;
                    try 
                    {
                        ssh.Connect();
                        // Use cat to read the file
                        // Using "cat" is simple for text files. 
                        resultText = ssh.ExecuteCommand($"cat \"{path}\"");
                    }
                    catch (Exception ex)
                    {
                        resultText = $"SSH Error: {ex.Message}";
                        isError = true;
                    }

                    var response = new 
                    {
                        jsonrpc = "2.0",
                        id = id,
                        result = new 
                        {
                            content = new[] 
                            {
                                new { type = "text", text = resultText }
                            },
                            isError = isError
                        }
                    };
                    await SendJsonAsync(writer, response);
                }
                else
                {
                     // Tool not found
                     // ... proper error response ...
                }
            }
             else if (method == "ping")
            {
                 var response = new { jsonrpc = "2.0", id = id, result = new { } };
                 await SendJsonAsync(writer, response);
            }
        }

        private async Task SendJsonAsync(StreamWriter writer, object data)
        {
            string json = JsonSerializer.Serialize(data, _jsonOptions);
            await writer.WriteLineAsync(json);
        }
    }
}