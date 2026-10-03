import type { ApiFailure, Hold, InventoryItem } from "./types";

const base = import.meta.env.VITE_API_BASE_URL ?? "";

export class ApiRequestError extends Error {
  status: number;
  errorCode?: string;

  constructor(message: string, status: number, errorCode?: string) {
    super(message);
    this.status = status;
    this.errorCode = errorCode;
  }
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`${base}${path}`, {
    ...init,
    headers: {
      Accept: "application/json",
      ...(init?.body ? { "Content-Type": "application/json" } : {}),
      ...init?.headers
    }
  });

  if (response.status === 204) {
    return undefined as T;
  }

  const text = await response.text();
  const body = text ? (JSON.parse(text) as T & ApiFailure) : null;
  if (!response.ok) {
    const failure = body as ApiFailure | null;
    throw new ApiRequestError(failure?.message || "The request failed.", response.status, failure?.errorCode);
  }

  return body as T;
}

export function getInventory() {
  return request<InventoryItem[]>("/api/inventory");
}

export function getActiveHolds() {
  return request<Hold[]>("/api/holds?status=active");
}

export function createHold(items: { productId: string; quantity: number }[]) {
  return request<Hold>("/api/holds", {
    method: "POST",
    body: JSON.stringify({ items })
  });
}

export function releaseHold(holdId: string) {
  return request<void>(`/api/holds/${encodeURIComponent(holdId)}`, {
    method: "DELETE"
  });
}
