# contracts/

Types that cross the wire, shared byte-for-byte by the server and the browser client:
opcodes, Nalix packets, DTOs, enums and `NosumProtocol.Version`. **No logic.**

Layout: `Nosum.Contracts/{Domain}/{C2S,S2C}/` plus one `*OpCode` class per domain.
Rules for packet classes: [`nalix-packets`](../.claude/skills/nalix-packets/SKILL.md).

Domains and opcode bands (record a new band here and in its `*OpCode` class):

| Domain | Band | Packets |
| :--- | :--- | :--- |
| Account | `0x1000-0x10FF` | register, login, resume session, set language; replies carry `AccountStatus` codes |

```bash
dotnet build contracts/Nosum.Contracts.slnx
```
