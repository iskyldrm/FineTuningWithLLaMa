# 09 System Architecture

## Related Nodes
- [[APEX]]
- [[02-agent-harness]]
- [[05-agent-teams-native]]
- [[10-model-fit-and-limits]]
- [[../notes/vector-db]]
- [[../notes/mcp]]

## Ozet
Hedef sistem: Orchestrator + role-based agents + shared memory + tool layer + task queue.

## Zorunlu Katmanlar
1. Orchestrator (gorev dagitimi, state yonetimi)
2. Memory (long-term ve short-term)
3. Task sistemi (queue ve async execution)
4. Tool layer (Git, CI/CD, DB, monitoring)
5. Agent rolleri (dev, tester, reviewer, PM, support)
