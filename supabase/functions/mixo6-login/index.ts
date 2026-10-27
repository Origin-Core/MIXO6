import { createClient } from "npm:@supabase/supabase-js@2";

const corsHeaders = {
  "Access-Control-Allow-Origin": "*",
  "Access-Control-Allow-Headers": "authorization, x-client-info, apikey, content-type",
  "Access-Control-Allow-Methods": "POST, OPTIONS",
  "Content-Type": "application/json; charset=utf-8",
};

const supabaseUrl = Deno.env.get("SUPABASE_URL") ?? "";
const publicKey = Deno.env.get("SUPABASE_ANON_KEY") ?? "";
// Add a NEW secret key in Supabase Function Secrets after revoking any exposed secret.
const privilegedKey = Deno.env.get("MIXO6_SUPABASE_SECRET_KEY") ?? Deno.env.get("SUPABASE_SERVICE_ROLE_KEY") ?? "";

const genericLoginError = { message: "Invalid login credentials" };
const admin = supabaseUrl && privilegedKey
  ? createClient(supabaseUrl, privilegedKey, { auth: { persistSession: false, autoRefreshToken: false } })
  : null;
const userAuth = supabaseUrl && publicKey
  ? createClient(supabaseUrl, publicKey, { auth: { persistSession: false, autoRefreshToken: false } })
  : null;

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: corsHeaders });
}

async function sha256Hex(input: string): Promise<string> {
  const digest = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(input));
  return [...new Uint8Array(digest)].map((b) => b.toString(16).padStart(2, "0")).join("");
}

async function hmacIp(ip: string): Promise<string> {
  const key = await crypto.subtle.importKey(
    "raw", new TextEncoder().encode(privilegedKey), { name: "HMAC", hash: "SHA-256" }, false, ["sign"]
  );
  const digest = await crypto.subtle.sign("HMAC", key, new TextEncoder().encode(ip));
  return [...new Uint8Array(digest)].map((b) => b.toString(16).padStart(2, "0")).join("");
}

Deno.serve(async (req: Request) => {
  if (req.method === "OPTIONS") return new Response("ok", { headers: corsHeaders });
  if (req.method !== "POST") return json({ message: "Method not allowed" }, 405);
  if (!admin || !userAuth || !privilegedKey) return json({ message: "Login service is not configured" }, 503);

  let body: { username?: unknown; password?: unknown };
  try { body = await req.json(); } catch { return json(genericLoginError, 400); }
  const username = typeof body.username === "string" ? body.username.trim().toLowerCase() : "";
  const password = typeof body.password === "string" ? body.password : "";
  if (!/^[a-z0-9_]{3,20}$/.test(username) || password.length < 1 || password.length > 128)
    return json(genericLoginError, 400);

  const ip = (req.headers.get("cf-connecting-ip") ?? req.headers.get("x-forwarded-for")?.split(",")[0] ?? "").trim();
  if (!ip) return json({ message: "Could not safely identify request source; try signing in with email" }, 503);

  // Salt the IP hash with the server-only key. Raw client IPs are never stored.
  const ipHash = await hmacIp(ip);
  const { data: allowed, error: throttleError } = await admin.rpc("mixo6_consume_username_login_attempt", {
    p_ip_hash: ipHash,
    p_max_attempts: 10,
    p_window_seconds: 900,
  });
  if (throttleError) return json({ message: "Username login is not configured yet; deploy the MIXO6 SQL migration and function secrets" }, 503);
  if (allowed !== true) return json({ message: "Too many login attempts. Try again in 15 minutes." }, 429);

  let email: string | null = null;
  const { data: profile, error: profileError } = await admin
    .from("profiles").select("id").eq("username", username).maybeSingle();
  if (!profileError && profile?.id) {
    const { data: userResult } = await admin.auth.admin.getUserById(profile.id);
    email = userResult?.user?.email ?? null;
  }

  // Always make an Auth attempt; unknown usernames are not distinguished from wrong passwords.
  const targetEmail = email ?? `missing-${await sha256Hex(username)}@invalid.invalid`;
  const { data, error } = await userAuth.auth.signInWithPassword({ email: targetEmail, password });
  if (error || !data.session || !data.user || !email) return json(genericLoginError, 401);

  const session = data.session;
  return json({
    access_token: session.access_token,
    token_type: session.token_type,
    expires_in: session.expires_in,
    expires_at: session.expires_at,
    refresh_token: session.refresh_token,
    user: data.user,
  });
});
