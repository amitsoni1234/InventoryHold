# AI usage

This file records how AI was used to build the Inventory Hold assignment. It is an audit of this implementation session, not a template.

## 1. AI strategy

Tool: Cursor, with the coding agent reading both source documents before writing code:

- `MASTER-PROMPT.md` (implementation checklist, error codes, concurrency rules, documentation outline)
- `Senior Assignment - Senior Software Engineer (1).pdf` (Kibo selection assignment)

Context given to the agent:

- The PDF is the assignment. The markdown file is a stricter build spec derived from it. Where the markdown adds a concrete rule that still satisfies the PDF, that rule was implemented. Examples: `GET /api/holds?status=active`, a 10-second inventory cache TTL, and the default 15-minute hold.
- The required project names and dependency direction were fixed up front: Contracts and Domain stay free of MongoDB, Redis, and RabbitMQ.
- The machine had the .NET 9 SDK installed system-wide. The agent installed the .NET 10 SDK (10.0.401) for the user account so the solution could target `net10.0` as required, instead of quietly downgrading.

Prompt structure used for the build:

1. Extract the PDF text and read the markdown spec in full.
2. Choose the transaction boundary before writing controllers.
3. Implement domain services against interfaces, then infrastructure, then a thin HTTP layer.
4. Write unit tests against fakes only.
5. Compile, run the tests, then build the frontend.

## 2. Human audit

The person who requested the work asked to follow both files and did not separately accept or reject individual code suggestions in this session. The decisions below are the ones a reviewer should check. They are recorded because they change behavior, not because a later human edit already happened.

Accepted from the spec, and implemented as specified:

- Conditional `availableQuantity >= requested` updates inside a MongoDB transaction for every hold, including a one-line hold. A transaction is stricter than the single-document minimum and keeps the hold insert in the same commit.
- Duplicate product ids return 400 rather than being merged.
- Compare-and-set `Active -> Released` and `Active -> Expired`, so only one caller restores stock and publishes.
- Redis is used only for `GET /api/inventory`. Hold creation never reads the cache.
- Events are published only after the transaction commits. Broker failure is logged and does not roll back the hold.
- xUnit, React context-free component state, and a frontend that runs with `npm run dev` outside Compose.

Rejected or narrowed while implementing:

- Read-then-write stock checks were not used as the deduction mechanism. A pre-read loads product names and detects missing products. The quantity decision is the conditional update.
- Retrying a transaction after `UnknownTransactionCommitResult` was rejected. The transaction may already have committed, and a retry could create a second hold.
- Putting the React app in Compose was rejected. The assignment allows `npm run dev`, and keeping the UI outside Compose makes API logs easier to follow.
- Declaring consumer queues in the publisher was kept only as inspectable local queues (`inventory.holds.created`, `.released`, `.expired`). The README tells a production consumer to own its queue. An empty exchange with no queues would make the assignment harder to demonstrate.
- The original weather-forecast template that was in this folder was left on disk and is not part of the solution. The runnable solution is `InventoryHold.slnx`.

## 3. Verification

The agent generated the unit tests in `src/InventoryHold.UnitTests` from the required case list: validation, missing product, insufficient stock, successful create, `HoldCreated`, release, restoration, already released, expiry, compare-and-set loss, and cache behavior. Extra cases cover idempotent `clientRequestId`, delete-of-a-due-hold, the expiration sweep, and active-list filtering.

Validation performed in this session:

- `dotnet build` and `dotnet test` on `InventoryHold.slnx` with the .NET 10 SDK. 21 tests passed. They do not connect to MongoDB, Redis, or RabbitMQ.
- `tsc --noEmit` in `frontend`.
- `docker-compose up --build` started MongoDB, Redis, RabbitMQ, and the API. Checked create, active list, inventory deduction, release, stock restoration, 400 for an empty item list, and 409 for insufficient stock and a second release.
- RabbitMQ queues `inventory.holds.created` and `inventory.holds.released` received messages.
- The React page at http://localhost:5173 showed the seeded inventory, placed a hold, and updated the available quantity and active-hold list without a manual reload. The release confirmation dialog opened.

Defect found during that check and fixed: a unique sparse index on `ClientRequestId` still indexed explicit `null`, so the second hold with no client key returned 500. The document now omits the field when no key is supplied (`BsonIgnoreIfNull`). A follow-up create succeeded.

The test fakes encode the store contract (conditional deduct, single winner of a transition). They do not prove the MongoDB filter itself. That behavior is in `MongoInventoryHoldStore` and is described in `README.md`.
