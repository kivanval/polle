import { AuthLayout } from './AuthLayout'
import { GuestRoute } from '@/features/auth/GuestRoute'
export function RegisterPage() {
  return <GuestRoute><AuthLayout mode="register" /></GuestRoute>
}
