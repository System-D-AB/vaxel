# docs/ — the framework contract

This folder is the **constitution** of vaxel: principles, architecture, wire protocol, .NET API, security, testing and Datastar parity.

It used to be called `spec/`. It was renamed so implementation packets could live in [`specs/`](../specs/README.md) without colliding with the contract.

| Rule | Meaning |
|---|---|
| Change it when the idea was wrong | Same commit as the code, plus a [CHANGELOG](../CHANGELOG.md) entry that says *why* |
| Do not put user stories here | Those belong in `specs/<slice>/requirements.md` |
| Do not duplicate it in a packet | Packet `design.md` **cites** these chapters |

Start at [01 — Principles](01-principles.md). Implement from [specs/](../specs/README.md).
