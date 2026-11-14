export type AuthValues = { email: string; password: string; confirmPassword: string }
export type AuthErrors = Partial<Record<keyof AuthValues, string>>

export function validateAuth(values: AuthValues, registering: boolean): AuthErrors {
  const errors: AuthErrors = {}
  const email = values.email.trim()
  if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email) || email.length > 254) errors.email = 'Enter a valid email address.'
  if (!values.password || values.password.length > 128) errors.password = 'Enter a password of up to 128 characters.'
  else if (registering && (values.password.length < 12 || !/[A-Z]/.test(values.password) || !/[a-z]/.test(values.password) || !/[0-9]/.test(values.password))) {
    errors.password = 'Use 12–128 characters, including uppercase, lowercase and a number.'
  }
  if (registering && values.confirmPassword !== values.password) errors.confirmPassword = 'Your passwords do not match.'
  return errors
}
