# Linux MCP Server Agent

A Model Context Protocol (MCP) server implementation in C# that acts as a secure bridge to a Linux environment via SSH. This agent allows LLMs (via MCP Clients like Claude Desktop) to execute commands and read files on a Linux machine.

## Features

*   **MCP Support**: Implements the Model Context Protocol (JSON-RPC 2.0 via Stdio) to expose tools to AI agents.
*   **SSH Integration**: Connects to remote (or local) Linux servers securely using `SSH.NET`.
*   **Local Test Environment**: Includes a Docker setup (Dockerfile & Compose) to spin up a safe, sandboxed Ubuntu "box" for testing.
*   **Interactive CLI**: A built-in mode to test SSH connections and run commands manually before connecting an AI.

## Prerequisites

*   **.NET SDK** (Targeting `net10.0` as per project configuration)
*   **Docker Desktop** (for running the local test environment)

## Setup

### 1. Clone & Restore
```bash
git clone <repo-url>
cd LinuxMcpServer
dotnet restore
```

### 2. Configuration
Create an `appsettings.json` file in the project root to store your SSH credentials.
*(Note: This file is ignored by git for security).*

**Example `appsettings.json`:**
```json
{
  "SSH_HOST": "localhost",
  "SSH_PORT": 2222,
  "SSH_USER": "testuser",
  "SSH_PASS": "testpass",
  "OLLAMA_URL": "http://localhost:11434",
  "OLLAMA_MODEL": "llama3"
}
```

### 3. Start the Local Linux Box
If you don't have a remote server, use the included Docker environment.
```bash
docker-compose up -d --build
```
This launches an Ubuntu container listening on port `2222` with the credentials `testuser` / `testpass`.

## Usage

### Interactive Mode (Connection Test)
Before hooking it up to an AI, verify your connection works:
```bash
dotnet run -- --interactive
```
You should see a prompt `>`. Try typing `whoami` or `ls -la`.

### Interactive Smart Mode (Connection Test)
Before hooking it up to an AI, verify your connection works. This mode uses **Ollama** locally to translate natural language into Linux commands.

1.  **Start Ollama**: Use a separate terminal to run the model.
    ```bash
    ollama run llama3
    ```

2.  **Run the Agent**:
    ```bash
    dotnet run -- --interactive
    ```

3.  **Chat**:
    You can now ask questions in English!
    ```text
    > Check if I have internet
    [Ollama] Translated to: ping -c 4 google.com
    [Output] ...
    ```

### Running as an MCP Server
To use this with an MCP Client (like Claude Desktop), configure the client to run this application.

**Command**: `dotnet`
**Args**: `run --project /absolute/path/to/LinuxMcpServer.csproj`

Or release build:
`dotnet run --configuration Release`

## Available Tools

The server exposes the following tools to the LLM:

### 1. `linux_command`
Executes a specific shell command on the connected Linux machine.
*   **Input**: `command` (string)
*   **Returns**: Standard Output (stdout) or Standard Error (stderr) if it fails.

### 2. `read_file`
Reads the complete contents of a file.
*   **Input**: `path` (string) - Absolute path to the file.
*   **Returns**: File content as text.

## Architecture

*   **`Program.cs`**: Contains the MCP Protocol loop (reading JSON-RPC from Stdio) and the tool definitions.
*   **`SshService.cs`**: Manages the SSH session using the `SSH.NET` library.
*   **`Dockerfile.linux-box`**: Defines the clean Ubuntu environment for testing.
