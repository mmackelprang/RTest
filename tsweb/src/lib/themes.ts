import type { ThemeName } from '@/types'

export interface ThemeColors {
  background: string
  foreground: string
  card: string
  cardForeground: string
  popover: string
  popoverForeground: string
  primary: string
  primaryForeground: string
  secondary: string
  secondaryForeground: string
  muted: string
  mutedForeground: string
  accent: string
  accentForeground: string
  destructive: string
  destructiveForeground: string
  border: string
  input: string
  ring: string
  ledAmber: string
  ledCyan: string
  ledGreen: string
}

export const themes: Record<ThemeName, ThemeColors> = {
  dark: {
    background: 'oklch(0.2 0.01 240)',
    foreground: 'oklch(0.98 0 0)',
    card: 'oklch(0.25 0.02 240)',
    cardForeground: 'oklch(0.98 0 0)',
    popover: 'oklch(0.25 0.02 240)',
    popoverForeground: 'oklch(0.98 0 0)',
    primary: 'oklch(0.75 0.15 195)',
    primaryForeground: 'oklch(0.2 0.01 240)',
    secondary: 'oklch(0.3 0.02 240)',
    secondaryForeground: 'oklch(0.98 0 0)',
    muted: 'oklch(0.3 0.02 240)',
    mutedForeground: 'oklch(0.5 0.02 240)',
    accent: 'oklch(0.75 0.15 195)',
    accentForeground: 'oklch(0.2 0.01 240)',
    destructive: 'oklch(0.577 0.245 27.325)',
    destructiveForeground: 'oklch(0.98 0 0)',
    border: 'oklch(0.35 0.02 240)',
    input: 'oklch(0.35 0.02 240)',
    ring: 'oklch(0.75 0.15 195)',
    ledAmber: 'oklch(0.8 0.18 75)',
    ledCyan: 'oklch(0.7 0.15 195)',
    ledGreen: 'oklch(0.75 0.18 140)',
  },
  light: {
    background: 'oklch(0.98 0 0)',
    foreground: 'oklch(0.15 0 0)',
    card: 'oklch(1 0 0)',
    cardForeground: 'oklch(0.15 0 0)',
    popover: 'oklch(1 0 0)',
    popoverForeground: 'oklch(0.15 0 0)',
    primary: 'oklch(0.45 0.2 240)',
    primaryForeground: 'oklch(0.98 0 0)',
    secondary: 'oklch(0.92 0.01 240)',
    secondaryForeground: 'oklch(0.15 0 0)',
    muted: 'oklch(0.92 0.01 240)',
    mutedForeground: 'oklch(0.45 0.01 240)',
    accent: 'oklch(0.92 0.01 240)',
    accentForeground: 'oklch(0.15 0 0)',
    destructive: 'oklch(0.55 0.25 25)',
    destructiveForeground: 'oklch(0.98 0 0)',
    border: 'oklch(0.88 0.01 240)',
    input: 'oklch(0.88 0.01 240)',
    ring: 'oklch(0.45 0.2 240)',
    ledAmber: 'oklch(0.6 0.2 60)',
    ledCyan: 'oklch(0.5 0.18 195)',
    ledGreen: 'oklch(0.55 0.2 140)',
  },
  nord: {
    background: 'oklch(0.26 0.03 250)',
    foreground: 'oklch(0.88 0.01 220)',
    card: 'oklch(0.29 0.03 250)',
    cardForeground: 'oklch(0.88 0.01 220)',
    popover: 'oklch(0.29 0.03 250)',
    popoverForeground: 'oklch(0.88 0.01 220)',
    primary: 'oklch(0.65 0.12 225)',
    primaryForeground: 'oklch(0.26 0.03 250)',
    secondary: 'oklch(0.33 0.03 250)',
    secondaryForeground: 'oklch(0.88 0.01 220)',
    muted: 'oklch(0.33 0.03 250)',
    mutedForeground: 'oklch(0.55 0.02 230)',
    accent: 'oklch(0.70 0.14 180)',
    accentForeground: 'oklch(0.26 0.03 250)',
    destructive: 'oklch(0.58 0.18 15)',
    destructiveForeground: 'oklch(0.88 0.01 220)',
    border: 'oklch(0.36 0.03 250)',
    input: 'oklch(0.36 0.03 250)',
    ring: 'oklch(0.65 0.12 225)',
    ledAmber: 'oklch(0.78 0.15 75)',
    ledCyan: 'oklch(0.70 0.14 180)',
    ledGreen: 'oklch(0.72 0.16 145)',
  },
  dracula: {
    background: 'oklch(0.24 0.02 265)',
    foreground: 'oklch(0.93 0.01 90)',
    card: 'oklch(0.28 0.02 265)',
    cardForeground: 'oklch(0.93 0.01 90)',
    popover: 'oklch(0.28 0.02 265)',
    popoverForeground: 'oklch(0.93 0.01 90)',
    primary: 'oklch(0.75 0.17 330)',
    primaryForeground: 'oklch(0.24 0.02 265)',
    secondary: 'oklch(0.32 0.02 265)',
    secondaryForeground: 'oklch(0.93 0.01 90)',
    muted: 'oklch(0.32 0.02 265)',
    mutedForeground: 'oklch(0.56 0.02 265)',
    accent: 'oklch(0.78 0.19 140)',
    accentForeground: 'oklch(0.24 0.02 265)',
    destructive: 'oklch(0.62 0.24 15)',
    destructiveForeground: 'oklch(0.93 0.01 90)',
    border: 'oklch(0.36 0.02 265)',
    input: 'oklch(0.36 0.02 265)',
    ring: 'oklch(0.75 0.17 330)',
    ledAmber: 'oklch(0.82 0.17 75)',
    ledCyan: 'oklch(0.73 0.16 195)',
    ledGreen: 'oklch(0.78 0.19 140)',
  },
  solarized: {
    background: 'oklch(0.18 0.04 205)',
    foreground: 'oklch(0.65 0.05 195)',
    card: 'oklch(0.22 0.04 205)',
    cardForeground: 'oklch(0.65 0.05 195)',
    popover: 'oklch(0.22 0.04 205)',
    popoverForeground: 'oklch(0.65 0.05 195)',
    primary: 'oklch(0.55 0.14 230)',
    primaryForeground: 'oklch(0.18 0.04 205)',
    secondary: 'oklch(0.26 0.04 205)',
    secondaryForeground: 'oklch(0.65 0.05 195)',
    muted: 'oklch(0.26 0.04 205)',
    mutedForeground: 'oklch(0.48 0.05 195)',
    accent: 'oklch(0.63 0.16 180)',
    accentForeground: 'oklch(0.18 0.04 205)',
    destructive: 'oklch(0.55 0.20 20)',
    destructiveForeground: 'oklch(0.65 0.05 195)',
    border: 'oklch(0.30 0.04 205)',
    input: 'oklch(0.30 0.04 205)',
    ring: 'oklch(0.55 0.14 230)',
    ledAmber: 'oklch(0.75 0.16 65)',
    ledCyan: 'oklch(0.63 0.16 180)',
    ledGreen: 'oklch(0.70 0.18 140)',
  },
  monokai: {
    background: 'oklch(0.20 0.01 120)',
    foreground: 'oklch(0.93 0.01 90)',
    card: 'oklch(0.24 0.01 120)',
    cardForeground: 'oklch(0.93 0.01 90)',
    popover: 'oklch(0.24 0.01 120)',
    popoverForeground: 'oklch(0.93 0.01 90)',
    primary: 'oklch(0.72 0.20 330)',
    primaryForeground: 'oklch(0.20 0.01 120)',
    secondary: 'oklch(0.28 0.01 120)',
    secondaryForeground: 'oklch(0.93 0.01 90)',
    muted: 'oklch(0.28 0.01 120)',
    mutedForeground: 'oklch(0.54 0.01 120)',
    accent: 'oklch(0.76 0.22 140)',
    accentForeground: 'oklch(0.20 0.01 120)',
    destructive: 'oklch(0.60 0.24 15)',
    destructiveForeground: 'oklch(0.93 0.01 90)',
    border: 'oklch(0.32 0.01 120)',
    input: 'oklch(0.32 0.01 120)',
    ring: 'oklch(0.72 0.20 330)',
    ledAmber: 'oklch(0.80 0.18 75)',
    ledCyan: 'oklch(0.71 0.17 195)',
    ledGreen: 'oklch(0.76 0.22 140)',
  },
  gruvbox: {
    background: 'oklch(0.22 0.03 50)',
    foreground: 'oklch(0.88 0.03 75)',
    card: 'oklch(0.26 0.03 50)',
    cardForeground: 'oklch(0.88 0.03 75)',
    popover: 'oklch(0.26 0.03 50)',
    popoverForeground: 'oklch(0.88 0.03 75)',
    primary: 'oklch(0.68 0.15 140)',
    primaryForeground: 'oklch(0.22 0.03 50)',
    secondary: 'oklch(0.30 0.03 50)',
    secondaryForeground: 'oklch(0.88 0.03 75)',
    muted: 'oklch(0.30 0.03 50)',
    mutedForeground: 'oklch(0.56 0.03 60)',
    accent: 'oklch(0.72 0.17 75)',
    accentForeground: 'oklch(0.22 0.03 50)',
    destructive: 'oklch(0.58 0.20 25)',
    destructiveForeground: 'oklch(0.88 0.03 75)',
    border: 'oklch(0.34 0.03 50)',
    input: 'oklch(0.34 0.03 50)',
    ring: 'oklch(0.68 0.15 140)',
    ledAmber: 'oklch(0.77 0.16 75)',
    ledCyan: 'oklch(0.69 0.14 195)',
    ledGreen: 'oklch(0.72 0.17 140)',
  },
}

export function applyTheme(themeName: ThemeName) {
  const theme = themes[themeName]
  const root = document.documentElement
  
  Object.entries(theme).forEach(([key, value]) => {
    const cssVarName = key.replace(/([A-Z])/g, '-$1').toLowerCase()
    root.style.setProperty(`--${cssVarName}`, value)
  })
}
