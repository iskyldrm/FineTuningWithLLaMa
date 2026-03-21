# Sprint 1 - Mart Ayi (March)

## Related Nodes
- [[APEX]]
- [[../roadmap/01-foundations]]
- [[../roadmap/02-agents]]
- [[../roadmap/03-finetuning]]

## Sprint Bilgileri

| Alan | Detay |
|------|-------|
| Sprint No | Sprint 1 |
| Ay | Mart 2026 |
| Baslangic | 2026-03-01 |
| Bitis | 2026-03-31 |
| Durum | Devam Ediyor |

## Hedef

Mart ayi Sprint 1 kapsaminda projenin temel altyapisini kurmak:

1. LLM fine-tuning notebook'unu tamamlamak ve test etmek
2. Temel ajan mimarisi taslagi olusturmak
3. AI OS dokumantasyonunu duzenlemek
4. Roadmap fazlarini planlamak

---

## Gorevler

### 1. Fine-Tuning Notebook

- [ ] `example_colab_finetune_llama.ipynb` uzerinde GPU testleri calistir
- [ ] LoRA parametrelerini optimize et
- [ ] MiniMax-M2.5 model ciktisini kaydet ve degerlendirme yap
- [ ] Egitim metriklerini (loss, accuracy) kaydet

### 2. Temel AI Altyapisi (Foundations)

- [ ] LLM temel kavramlarini belgele ([01-foundations](../roadmap/01-foundations.md))
- [ ] Prompt tasarim rehberi hazirla
- [ ] Basit bir FastAPI servisi olustur (model inference endpoint)
- [ ] Ollama ile lokal model testleri yap ([ollama notu](../notes/ollama.md))

### 3. Ajan Mimarisi Taslagi (Agents)

- [ ] Orchestrator + Agent rolleri tanimla ([02-agents](../roadmap/02-agents.md))
- [ ] MCP katmani entegrasyon planinı ciz ([mcp notu](../notes/mcp.md))
- [ ] Telegram bot prototipini baslat ([telegram-agent](../projects/telegram-agent.md))
- [ ] Gorev dagitim mantigi (task queue) tasarla

### 4. Dokumantasyon ve Bilgi Tabani

- [ ] `APEX.md` sprint referanslarini ekle
- [ ] `ai-system/insights/` notlarini gozden gecir ve guncelle
- [ ] Eksik roadmap bolumlerini tamamla
- [ ] Sprint retrospektif notu hazirla

---

## Sprint Retrospektif

*(Sprint sonunda doldurulacak)*

### Neler Iyi Gitti?
-

### Neler Gelistirilebilir?
-

### Bir Sonraki Sprinte Tasinan Gorevler
-

---

## Kaynaklar

- [Hugging Face PEFT](https://github.com/huggingface/peft)
- [TRL - Transformer Reinforcement Learning](https://github.com/huggingface/trl)
- [MiniMax-M2.5 Model](https://huggingface.co/MiniMaxAI/MiniMax-M2.5)
- [LangChain Agents](https://docs.langchain.com/docs/components/agents)
- [Model Context Protocol](https://modelcontextprotocol.io)
