import { AuthLayout } from './AuthLayout'
import { GuestRoute } from '@/features/auth/GuestRoute'
export function LoginPage() {
  return <GuestRoute><AuthLayout mode="login" /></GuestRoute>
}
