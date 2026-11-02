# ADR-123: A tool too weak for a node is refused as its own status

## Status

Accepted

Amends [ADR-026](ADR-026-tool-upgrades-and-the-mine.md) D1 in its refusal only: the tier rule is unchanged. Wire change: `FarmStatus.WeakTool` (25) is
appended and `NosumProtocol.Version` goes to 35. Server and client ship together.

## Context

Round four of the gathering critique (`docs/design/critique-gathering.md`, section 7e) found that a refused swing reads the same whatever the reason. A
copper pickaxe on iron ore, a copper pickaxe on a stone, and a pickaxe on a tree all reply `FarmStatus.WrongTool`, and the line reads "This tool does not
work on that." A player holding a copper pickaxe is told the tool is the wrong kind, when the fix is an iron pickaxe from the blacksmith. The server knows
the difference: `NodeRules.Hit` checks the kind first and the tier second, and both return the same outcome.

## Decision

- **D1 - The tier shortfall is its own outcome.** `NodeHitOutcome.TooWeak` (3) is returned when the held tool is the node's kind and the tier is below
  `NodeDefinition.RequiredTier`. A tool of the wrong kind, and a ladder, stay `WrongTool`. The node is unchanged in both cases and no energy is spent, as
  before. The server maps `TooWeak` to `FarmStatus.WeakTool`.
- **D2 - The line names the tool in hand.** "Your {tool} is too weak for that. Upgrade it at the blacksmith." (vi "{tool} chưa đủ sức cho thứ này. Nâng cấp
  ở thợ rèn nhé."). The name is the held slot's item, read the way ADR-121 D2 reads a seed. With nothing held, the line falls back to the wrong-tool text.
- **D3 - The wire change is an appended enum value.** `FarmStatus.WeakTool` = 25 is appended; no opcode or existing field changes. The protocol doc line for
  35 says so. A client built for 34 does not know the value, so the two ship together.
- **D4 - The node is not named.** "Iron ore needs an iron pickaxe" would need a name for each node kind (none exists in the resources; the names are the
  yield items') and a second route for the message, since `GameMessagesViewModel` cannot see the node the swing hit. That is a feature-sized change and is
  not in this round. The tool in hand and the upgrade step are enough to act on.

## Consequences

- A player with a copper pickaxe at iron ore reads the tool they hold and where to upgrade it, instead of "does not work on that".
- Only the refusal text and one status code change. The energy, the node, the yield and the XP are unchanged, and the tests that pinned the old outcome
  for a low tier now pin `TooWeak` (`NodeRulesTests`, `OutdoorRulesTests`). A wrong-kind test pins `WrongTool` at every tier.
- The version guard (`NosumProtocolVersionDocTests`) moves with the bump; the market test that named version 34 now checks it is at least 34.

## Revisit when

- Players ask which node a refusal means: add the node name (D4). It needs the name table and a message route that sees the node.
- A tier beyond iron is added: the line names the held tool and the blacksmith step still, so no change is needed.
