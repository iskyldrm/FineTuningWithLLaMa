# APEX Agent Team

This repository now contains a local-first multi-agent software team built around the original APEX markdown knowledge base.

## Stack

- ASP.NET Core Web API on .NET 10
- React + TypeScript + Vite frontend
- PostgreSQL for mission and activity persistence
- Qdrant for long-term knowledge retrieval
- Ollama for local chat + embedding models
- GitHub Issues as the V1 external task sink

## What the app does

- Reads `APEX.md` and linked markdown notes into a knowledge graph
- Starts missions that flow through Analyst, WebDev, Frontend, Backend, Tester, PM, and Support roles
- Streams live activity to the dashboard over SignalR
- Produces patch proposals and lets the operator approve or reject them
- Applies approved unified diffs with a patch-only git workflow, then runs validation

## Local setup

1. Install Ollama on Windows and pull the models:`r`n   - `ollama pull qwen2.5-coder:14b``r`n   - `ollama pull nomic-embed-text``r`n2. Configure `.env.example` if you want real GitHub issue creation.`r`n3. Start the full stack:`r`n   - `docker compose up -d --build``r`n4. Open the UI:`r`n   - `http://localhost:8080``r`n5. Optional direct endpoints:`r`n   - API `http://localhost:5000/api/dashboard``r`n   - Qdrant UI `http://localhost:6333/dashboard`

## Configuration notes

- API settings live in [appsettings.json](src/Apex.AgentTeam.Api/appsettings.json).
- `Workspace.RootPath` defaults to the repo root so patch application and validation run against this workspace.
- If GitHub credentials are missing, Analyst still creates a local external-task placeholder instead of failing the mission.
- If Ollama or Qdrant is unavailable, the system falls back to deterministic local summaries and embeddings so the dashboard still operates.

## Key endpoints

- `GET /api/dashboard`
- `POST /api/missions`
- `GET /api/missions/{missionId}`
- `GET /api/missions/{missionId}/activities`
- `POST /api/patches/{proposalId}/approve`
- `POST /api/patches/{proposalId}/reject`
- `SignalR /hubs/activity`
