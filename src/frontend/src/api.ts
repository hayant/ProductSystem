// Isolating the HTTP concerns here means the components only see a clean async API.
// If the backend contract changes, there's exactly one file to update.
//
// Both values are injected at build time via Vite env vars (.env.development
// for local dev; .env.production or App Service config for deployed builds).
// Fallback to the dev defaults so a forgotten env doesn't yield cryptic errors.
const API_BASE = import.meta.env.VITE_API_BASE ?? 'http://localhost:5080/api/v1';
const API_KEY = import.meta.env.VITE_API_KEY ?? '';

const authHeaders: HeadersInit = { 'X-Api-Key': API_KEY };

export interface Product {
  id: string;
  sku: string;
  name: string;
  price: number;
  updatedAt: string;
}

export interface ApiError {
  error: string;
  message: string;
}

export async function listProducts(): Promise<Product[]> {
  const res = await fetch(`${API_BASE}/products`, { headers: authHeaders });
  if (!res.ok) throw new Error(`Failed to load products: ${res.status}`);
  return res.json();
}

export async function createProduct(input: { sku: string; name: string; price: number }): Promise<Product> {
  const res = await fetch(`${API_BASE}/products`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', ...authHeaders },
    body: JSON.stringify(input),
  });
  if (!res.ok) {
    const body: ApiError = await res.json().catch(() => ({ error: 'unknown', message: 'Request failed' }));
    throw new Error(body.message);
  }
  return res.json();
}
