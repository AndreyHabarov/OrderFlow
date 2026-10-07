export interface User {
  id: string
  email: string
  role: string
}

export interface AuthResponse {
  accessToken: string
  accessTokenExpiresAt: string
  refreshToken: string
  user: User
}

export interface Product {
  id: string
  name: string
  description: string
  price: number
  currency: string
  stockQuantity: number
}

export interface Paged<T> {
  items: T[]
  page: number
  pageSize: number
  totalCount: number
  totalPages: number
}

export interface CartItem {
  productId: string
  quantity: number
  unitPrice: number
  lineTotal: number
}

export interface Cart {
  items: CartItem[]
  total: number
  currency: string
}

export interface OrderItem {
  productId: string
  productName: string
  quantity: number
  unitPrice: number
  lineTotal: number
}

export interface Order {
  id: string
  status: string
  total: number
  currency: string
  createdAt: string
  cancellationReason: string | null
  items: OrderItem[]
}
