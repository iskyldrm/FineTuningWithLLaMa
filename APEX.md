# FineTuningWithLLaMa

Bu repo iki ana parcayi birlestirir:

1. Colab uzerinde fine-tuning notebook'u
2. Markdown tabanli AI OS (not, roadmap, proje ve kaynak sistemi)

Aktif model ornegi: MiniMaxAI/MiniMax-M2.5

## Yeni Ogrenimler: MiniMax Agent Team Yaklasimi

Bu bolum, son ogrenim notlarinin ozetidir. Ana fikir: tek bir model degil, model + orchestrator + memory + tools + task sistemi ile AI organizasyonu kurmak.

### Kisa Sonuc

1. MiniMax yaklasimi, role-based multi-agent ekipler icin guclu bir temel sagliyor.
2. Self-improvement dongusu ile sistem kendi performansini otomatik artirabiliyor.
3. Basari model seciminden cok sistem mimarisine bagli.

### Ogrenim Linkleri (Alt Notlar)

- [ai-system/insights/01-agent-self-evolution.md](ai-system/insights/01-agent-self-evolution.md)
- [ai-system/insights/02-agent-harness.md](ai-system/insights/02-agent-harness.md)
- [ai-system/insights/03-rl-team-workflow.md](ai-system/insights/03-rl-team-workflow.md)
- [ai-system/insights/04-self-improvement-loop.md](ai-system/insights/04-self-improvement-loop.md)
- [ai-system/insights/05-agent-teams-native.md](ai-system/insights/05-agent-teams-native.md)
- [ai-system/insights/06-production-debugging.md](ai-system/insights/06-production-debugging.md)
- [ai-system/insights/07-office-cs-agents.md](ai-system/insights/07-office-cs-agents.md)
- [ai-system/insights/08-self-learning-core.md](ai-system/insights/08-self-learning-core.md)
- [ai-system/insights/09-system-architecture.md](ai-system/insights/09-system-architecture.md)
- [ai-system/insights/10-model-fit-and-limits.md](ai-system/insights/10-model-fit-and-limits.md)

### Mimari Ozet (Hedef Sistem)

```mermaid
graph TD
	O[Orchestrator] --> D[Dev Agent]
	O --> T[Tester Agent]
	O --> A[Analyst Agent]
	O --> P[PM Agent]
	O --> S[Support Agent]

	D --> M[Shared Memory + Tools]
	T --> M
	A --> M
	P --> M
	S --> M

	M --> LTM[Long-term Memory: Vector DB]
	M --> STM[Short-term Context Memory]
	M --> Q[Task Queue: RabbitMQ or Kafka]
	M --> TL[Tool Layer: Git CI-CD DB Monitoring]

	style O fill:#f9b,stroke:#333,stroke-width:2px
	style M fill:#9fd,stroke:#333,stroke-width:2px
```

## Hizli Baslangic

1. [example_colab_finetune_llama.ipynb](example_colab_finetune_llama.ipynb) dosyasini Colab'de ac.
2. GPU runtime secip hucreleri sirayla calistir.
3. AI OS klasorunu bu README altindaki linkler uzerinden yonet.

## AI Master Plan (Mermaid)

```mermaid
graph TD
	Root[AI Mastery and Enterprise Agent Project] --> Infra[Infrastructure and Data]
	Root --> Brain[Models and Intelligence]
	Root --> Orchestration[Workflows and Agents]
	Root --> Interfaces[Deployment and UX]

	Infra --> VDB[Vector Databases: Pinecone or Milvus or Qdrant]
	Infra --> RAG[RAG Systems and Data Ingestion]
	Infra --> MCP[Model Context Protocol Layers]

	Brain --> LLM[Base Models: GPT-4 or Claude or Llama]
	Brain --> FT[Fine-Tuning and Quantization]
	Brain --> Eval[Model Evaluation and Benchmarking]

	Orchestration --> N8N[Workflow Automation]
	Orchestration --> DevAgents[Software Dev Agents]
	Orchestration --> EntAgent[Enterprise AI Agent]

	Interfaces --> TG[Telegram Bot]
	Interfaces --> API[FastAPI or .NET WebAPI]
	Interfaces --> Dash[Admin Dashboard]

	style Root fill:#f96,stroke:#333,stroke-width:4px
```

## Obsidian Baglama

Obsidian'i bu repo ile birlikte kullanmak icin:

1. Obsidian -> Open folder as vault
2. Bu repo kok klasorunu sec
3. Ozellikle [ai-system](ai-system) altindaki markdown dosyalarini yonet
4. Notlar arasinda `[[wiki-link]]` kullan
5. Degisiklikleri Git ile commit edip push et

Not: `.obsidian` klasorunu repoya eklemek zorunlu degil. Cihaz ozel ayarlari local kalabilir.

## Neden LoRA?

1. Colab ortamina daha uygun bellek kullanimi
2. Tam fine-tuning'e gore daha hizli iterasyon
3. Kucuk adapter dosyalari ile daha kolay tasima ve versiyonlama

## Model Nerde Tutulmali?

Onerilen pratik:

1. GitHub: kod, notebook, metrik, dokuman
2. Harici depolama: model agirliklari (Hugging Face Hub, Drive, blob storage)

`models/` klasoru mantikli mi?

1. Sadece kucuk LoRA adapterlari icin evet
2. Tam model checkpointleri icin hayir
3. Mecbursa Git LFS kullan

## MiniMax-M2.5 Notu

MiniMax-M2.5 bazi Colab GPU tiplerinde VRAM sinirina takilabilir.
Gerektiginde daha guclu runtime sec veya daha kucuk modelle devam et.

## Aktif Sprint

- [Sprint 1 - Mart 2026](ai-system/sprints/sprint-1-march.md)

## Repo Icerigi

- [ai-system/sprints/sprint-1-march.md](ai-system/sprints/sprint-1-march.md): Sprint 1 - Mart 2026
- [example_colab_finetune_llama.ipynb](example_colab_finetune_llama.ipynb): Veri yukleme, egitme, cikti kaydetme
- [ai-system/roadmap/01-foundations.md](ai-system/roadmap/01-foundations.md)
- [ai-system/roadmap/02-agents.md](ai-system/roadmap/02-agents.md)
- [ai-system/roadmap/03-finetuning.md](ai-system/roadmap/03-finetuning.md)
- [ai-system/roadmap/04-rag.md](ai-system/roadmap/04-rag.md)
- [ai-system/roadmap/05-enterprise.md](ai-system/roadmap/05-enterprise.md)
- [ai-system/notes/ollama.md](ai-system/notes/ollama.md)
- [ai-system/notes/vector-db.md](ai-system/notes/vector-db.md)
- [ai-system/notes/mcp.md](ai-system/notes/mcp.md)
- [ai-system/projects/telegram-agent.md](ai-system/projects/telegram-agent.md)
- [ai-system/projects/n8n-ai-workflows.md](ai-system/projects/n8n-ai-workflows.md)
- [ai-system/projects/enterprise-ai.md](ai-system/projects/enterprise-ai.md)
- [ai-system/resources/links.md](ai-system/resources/links.md)
- [ai-system/resources/note-template.md](ai-system/resources/note-template.md)
- [ai-system/insights/01-agent-self-evolution.md](ai-system/insights/01-agent-self-evolution.md)
- [ai-system/insights/02-agent-harness.md](ai-system/insights/02-agent-harness.md)
- [ai-system/insights/03-rl-team-workflow.md](ai-system/insights/03-rl-team-workflow.md)
- [ai-system/insights/04-self-improvement-loop.md](ai-system/insights/04-self-improvement-loop.md)
- [ai-system/insights/05-agent-teams-native.md](ai-system/insights/05-agent-teams-native.md)
- [ai-system/insights/06-production-debugging.md](ai-system/insights/06-production-debugging.md)
- [ai-system/insights/07-office-cs-agents.md](ai-system/insights/07-office-cs-agents.md)
- [ai-system/insights/08-self-learning-core.md](ai-system/insights/08-self-learning-core.md)
- [ai-system/insights/09-system-architecture.md](ai-system/insights/09-system-architecture.md)
- [ai-system/insights/10-model-fit-and-limits.md](ai-system/insights/10-model-fit-and-limits.md)
