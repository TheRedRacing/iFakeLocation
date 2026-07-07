import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  // The .NET backend only ever serves pre-built static files -- there is no Node.js runtime
  // available when the packaged app runs, so this must always produce a static export.
  output: "export",
  images: {
    // No server available at runtime to run Next's image optimizer against.
    unoptimized: true,
  },
};

export default nextConfig;
