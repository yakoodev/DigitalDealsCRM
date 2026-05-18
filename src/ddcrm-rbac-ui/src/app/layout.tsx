import type { Metadata } from "next";
import type { CSSProperties } from "react";
import { QueryProvider } from "@/components/query-provider";
import { getThemeInitScript } from "@/lib/theme";
import "./globals.css";

const rootFontVariables: CSSProperties = {
  // Локальные fallback-шрифты, чтобы сборка не зависела от google fonts сети.
  ["--font-space-grotesk" as string]: "Segoe UI",
  ["--font-ibm-plex-mono" as string]: "Cascadia Mono",
};

export const metadata: Metadata = {
  title: "DigitalDeals CRM",
  description:
    "Dark CRM interface for projects, accounts, products, messages, workflows and admin control plane.",
  icons: {
    icon: [
      { url: "/brand/logoicon.svg", type: "image/svg+xml" },
      { url: "/brand/logoicon.png", type: "image/png" },
    ],
    apple: [{ url: "/brand/logoicon.png", type: "image/png" }],
  },
};

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  return (
    <html
      lang="ru"
      suppressHydrationWarning
      style={rootFontVariables}
    >
      <head>
        <script dangerouslySetInnerHTML={{ __html: getThemeInitScript() }} />
      </head>
      <body>
        <QueryProvider>{children}</QueryProvider>
      </body>
    </html>
  );
}
