export type InventoryItem = {
  productId: string;
  name: string;
  availableQuantity: number;
};

export type HoldItem = {
  productId: string;
  productName: string;
  quantity: number;
};

export type Hold = {
  holdId: string;
  status: "Active" | "Released" | "Expired" | string;
  createdAtUtc: string;
  expiresAtUtc: string;
  items: HoldItem[];
};

export type ApiFailure = {
  errorCode?: string;
  message?: string;
};
