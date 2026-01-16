using Renci.SshNet;
using System;

namespace LinuxMcpServer
{
    public class SshService : IDisposable
    {
        private SshClient _client = null!; // Initialize to null!, we handle connection in Connect()
        private readonly string _host;
        private readonly int _port;
        private readonly string _username;
        private readonly string _password;

        public SshService(string host, int port, string username, string password)
        {
            _host = host;
            _port = port;
            _username = username;
            _password = password;
        }

        public void Connect()
        {
            if (_client == null || !_client.IsConnected)
            {
                _client = new SshClient(_host, _port, _username, _password);
                _client.Connect();
            }
        }

        public string ExecuteCommand(string command)
        {
            // If we are in "Demo Mode" (no host provided), return a mock response
            if (_host == "demo" || _host == "localhost" && _username == "user") 
            {
                 // Small mock simulation for testing without a real Linux box
                 if (command.Trim() == "whoami") return "root";
                 if (command.Trim() == "ls") return "bin\netc\nhome\nvar";
                 if (command.Trim() == "uptime") return " 12:00:00 up 1 day,  1:00,  1 user,  load average: 0.00, 0.01, 0.05";
                 return $"[Demo Mode] Executed: {command}\n(Real execution requires a valid SSH_HOST)";
            }

            Connect();
            var cmd = _client.CreateCommand(command);
            var result = cmd.Execute();
            return result + (string.IsNullOrEmpty(cmd.Error) ? "" : "\nError: " + cmd.Error);
        }

        public void Dispose()
        {
            _client?.Disconnect();
            _client?.Dispose();
        }
    }
}
