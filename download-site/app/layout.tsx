import type { Metadata } from "next";
import { headers } from "next/headers";
import "./globals.css";

export async function generateMetadata(): Promise<Metadata> {
  const requestHeaders = await headers();
  const host = requestHeaders.get("host") ?? "localhost";
  const protocol = requestHeaders.get("x-forwarded-proto") ?? "https";
  const baseUrl = new URL(`${protocol}://${host}`);
  const socialImage = new URL("/og.png", baseUrl).toString();

  return {
    metadataBase: baseUrl,
    title: "Download Isley for Windows",
    description:
      "Download Isley 1.4.1 with calibrated visible-HUD tools, Live Map, authorized server telemetry, survival tools, and private voice.",
    icons: {
      icon: "/isley-triceratops-teeth-clean.png",
      shortcut: "/isley-triceratops-teeth-clean.png",
    },
    openGraph: {
      title: "Download Isley for Windows",
      description:
        "Calibrated visible-HUD tools, Live Map, authorized player awareness, Core Vitals, routes, and private push-to-talk voice.",
      type: "website",
      images: [{ url: socialImage, width: 1200, height: 630, alt: "Download Isley for Windows" }],
    },
    twitter: {
      card: "summary_large_image",
      title: "Download Isley for Windows",
      description: "Isley 1.4.1: calibrated visible-HUD tools and an authorized live awareness network for The Isle.",
      images: [socialImage],
    },
  };
}

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  return (
    <html lang="en">
      <body>{children}</body>
    </html>
  );
}
