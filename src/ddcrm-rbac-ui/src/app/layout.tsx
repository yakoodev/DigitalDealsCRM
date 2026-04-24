import type { Metadata } from "next";
import { IBM_Plex_Mono, Space_Grotesk } from "next/font/google";
import { QueryProvider } from "@/components/query-provider";
import "./globals.css";

const spaceGrotesk = Space_Grotesk({
  variable: "--font-space-grotesk",
  subsets: ["latin"],
});

const ibmPlexMono = IBM_Plex_Mono({
  variable: "--font-ibm-plex-mono",
  weight: ["400", "500"],
  subsets: ["latin"],
});

export const metadata: Metadata = {
  title: "DDCRM RBAC Console",
  description:
    "Базовый UI для DDCRM: role-aware операции проектов, аккаунтов, billing и gateway.",
};

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  return (
    <html
      lang="ru"
      className={`${spaceGrotesk.variable} ${ibmPlexMono.variable}`}
    >
      <body>
        <QueryProvider>{children}</QueryProvider>
      </body>
    </html>
  );
}
