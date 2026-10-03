import { FormEvent, useCallback, useEffect, useState } from "react";
import { createHold, getActiveHolds, getInventory, releaseHold } from "./api";
import type { Hold, InventoryItem } from "./types";

type DraftLine = {
  productId: string;
  quantity: string;
};

function errorMessage(error: unknown) {
  return error instanceof Error ? error.message : "Something went wrong.";
}

function TimeRemaining({ expiresAt }: { expiresAt: string }) {
  const [now, setNow] = useState(() => Date.now());

  useEffect(() => {
    const id = window.setInterval(() => setNow(Date.now()), 1000);
    return () => window.clearInterval(id);
  }, []);

  const remainingMs = new Date(expiresAt).getTime() - now;
  if (remainingMs <= 0) {
    return <span className="countdown due">Expired</span>;
  }

  const totalSeconds = Math.floor(remainingMs / 1000);
  const minutes = Math.floor(totalSeconds / 60);
  const seconds = totalSeconds % 60;
  return (
    <span className="countdown">
      {minutes}:{seconds.toString().padStart(2, "0")}
    </span>
  );
}

export function App() {
  const [inventory, setInventory] = useState<InventoryItem[]>([]);
  const [holds, setHolds] = useState<Hold[]>([]);
  const [lines, setLines] = useState<DraftLine[]>([{ productId: "", quantity: "1" }]);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [releasingId, setReleasingId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [pendingRelease, setPendingRelease] = useState<Hold | null>(null);

  const refresh = useCallback(async () => {
    const [items, active] = await Promise.all([getInventory(), getActiveHolds()]);
    setInventory(items);
    setHolds(active);
    setLines((current) => {
      if (current.some((line) => line.productId) || items.length === 0) {
        return current;
      }

      return [{ productId: items[0].productId, quantity: "1" }];
    });
  }, []);

  useEffect(() => {
    let active = true;
    refresh()
      .catch((err: unknown) => {
        if (active) setError(errorMessage(err));
      })
      .finally(() => {
        if (active) setLoading(false);
      });

    const id = window.setInterval(() => {
      refresh().catch((err: unknown) => setError(errorMessage(err)));
    }, 5000);

    return () => {
      active = false;
      window.clearInterval(id);
    };
  }, [refresh]);

  async function onCreate(event: FormEvent) {
    event.preventDefault();
    setError(null);
    setNotice(null);

    const items = lines.map((line) => ({
      productId: line.productId,
      quantity: Number(line.quantity)
    }));

    if (items.some((item) => !item.productId || !Number.isInteger(item.quantity) || item.quantity <= 0)) {
      setError("Choose a product and enter a positive whole-number quantity for every line.");
      return;
    }

    if (new Set(items.map((item) => item.productId)).size !== items.length) {
      setError("Each product can only appear once in a hold.");
      return;
    }

    setSaving(true);
    try {
      const hold = await createHold(items);
      await refresh();
      setNotice(`Hold ${hold.holdId.slice(0, 8)} placed.`);
      setLines([{ productId: inventory[0]?.productId ?? "", quantity: "1" }]);
    } catch (err: unknown) {
      setError(errorMessage(err));
    } finally {
      setSaving(false);
    }
  }

  async function confirmRelease() {
    if (!pendingRelease) return;
    setReleasingId(pendingRelease.holdId);
    setError(null);
    setNotice(null);
    try {
      await releaseHold(pendingRelease.holdId);
      await refresh();
      setNotice("Hold released and inventory restored.");
      setPendingRelease(null);
    } catch (err: unknown) {
      setError(errorMessage(err));
      await refresh().catch(() => undefined);
    } finally {
      setReleasingId(null);
    }
  }

  return (
    <div className="page">
      <header className="topbar">
        <div>
          <p className="eyebrow">Checkout reservation</p>
          <h1>Inventory Hold</h1>
        </div>
        <p className="lede">Place a temporary hold so stock cannot be sold twice. Active holds expire automatically.</p>
      </header>

      {error && (
        <div className="banner error" role="alert">
          <span>{error}</span>
          <button type="button" onClick={() => setError(null)}>
            Dismiss
          </button>
        </div>
      )}
      {notice && (
        <div className="banner ok" role="status">
          <span>{notice}</span>
          <button type="button" onClick={() => setNotice(null)}>
            Dismiss
          </button>
        </div>
      )}

      <main className="layout">
        <section className="panel">
          <div className="panel-head">
            <h2>Inventory</h2>
            {loading && <span className="muted">Loading…</span>}
          </div>
          <table>
            <thead>
              <tr>
                <th>Product</th>
                <th>Available</th>
              </tr>
            </thead>
            <tbody>
              {inventory.map((item) => (
                <tr key={item.productId}>
                  <td>{item.name}</td>
                  <td className={item.availableQuantity === 0 ? "qty empty" : "qty"}>{item.availableQuantity}</td>
                </tr>
              ))}
              {!loading && inventory.length === 0 && (
                <tr>
                  <td colSpan={2}>No products are available yet.</td>
                </tr>
              )}
            </tbody>
          </table>
        </section>

        <section className="panel">
          <div className="panel-head">
            <h2>Create hold</h2>
          </div>
          <form onSubmit={onCreate}>
            {lines.map((line, index) => (
              <div className="line" key={index}>
                <label>
                  Product
                  <select
                    value={line.productId}
                    onChange={(event) =>
                      setLines((current) =>
                        current.map((item, itemIndex) =>
                          itemIndex === index ? { ...item, productId: event.target.value } : item
                        )
                      )
                    }
                  >
                    <option value="">Select a product</option>
                    {inventory.map((item) => (
                      <option key={item.productId} value={item.productId}>
                        {item.name} ({item.availableQuantity} available)
                      </option>
                    ))}
                  </select>
                </label>
                <label>
                  Quantity
                  <input
                    type="number"
                    min={1}
                    step={1}
                    value={line.quantity}
                    onChange={(event) =>
                      setLines((current) =>
                        current.map((item, itemIndex) =>
                          itemIndex === index ? { ...item, quantity: event.target.value } : item
                        )
                      )
                    }
                  />
                </label>
                <button
                  type="button"
                  className="ghost"
                  disabled={lines.length === 1}
                  onClick={() => setLines((current) => current.filter((_, itemIndex) => itemIndex !== index))}
                >
                  Remove
                </button>
              </div>
            ))}
            <div className="actions">
              <button
                type="button"
                className="ghost"
                onClick={() => setLines((current) => [...current, { productId: "", quantity: "1" }])}
              >
                Add product
              </button>
              <button type="submit" className="primary" disabled={saving || loading}>
                {saving ? "Placing hold…" : "Place hold"}
              </button>
            </div>
          </form>
        </section>

        <section className="panel wide">
          <div className="panel-head">
            <h2>Active holds</h2>
            <span className="muted">{holds.length} open</span>
          </div>
          {holds.length === 0 && !loading && <p className="muted">No active holds. Placed holds show up here immediately.</p>}
          <div className="holds">
            {holds.map((hold) => (
              <article className="hold" key={hold.holdId}>
                <header>
                  <div>
                    <p className="hold-id">{hold.holdId}</p>
                    <p className="status">{hold.status}</p>
                  </div>
                  <TimeRemaining expiresAt={hold.expiresAtUtc} />
                </header>
                <ul>
                  {hold.items.map((item) => (
                    <li key={item.productId}>
                      <span>{item.productName}</span>
                      <strong>{item.quantity}</strong>
                    </li>
                  ))}
                </ul>
                <button type="button" onClick={() => setPendingRelease(hold)}>
                  Release hold
                </button>
              </article>
            ))}
          </div>
        </section>
      </main>

      {pendingRelease && (
        <div className="modal-backdrop" role="presentation">
          <div className="modal" role="dialog" aria-modal="true" aria-labelledby="release-title">
            <h3 id="release-title">Release this hold?</h3>
            <p>Stock for {pendingRelease.items.length} line{pendingRelease.items.length === 1 ? "" : "s"} will be restored immediately.</p>
            <div className="actions">
              <button type="button" className="ghost" onClick={() => setPendingRelease(null)} disabled={releasingId !== null}>
                Cancel
              </button>
              <button type="button" className="primary" onClick={confirmRelease} disabled={releasingId !== null}>
                {releasingId ? "Releasing…" : "Confirm release"}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
