# Cloudflare Emergency Maintenance Mode
## A reusable Worker-based maintenance page for a website with a failed origin

> **Example domain:** `examplemathsite.com`  
> **Example Worker:** `examplemathsite-maintenance`
>
> This manual uses a completely fictional domain so the procedure can be reused safely for any real website.

---

## Table of Contents

1. [What This Setup Does](#1-what-this-setup-does)
2. [How the Traffic Flow Changes](#2-how-the-traffic-flow-changes)
3. [Prerequisites](#3-prerequisites)
4. [Important: Apex Domain vs. Wildcard Routes](#4-important-apex-domain-vs-wildcard-routes)
5. [Create the Maintenance Worker](#5-create-the-maintenance-worker)
6. [Edit the Worker](#6-edit-the-worker)
7. [Deploy the Worker](#7-deploy-the-worker)
8. [Test the Worker Before Using Your Real Domain](#8-test-the-worker-before-using-your-real-domain)
9. [Verify Your DNS Record](#9-verify-your-dns-record)
10. [Create the Worker Route](#10-create-the-worker-route)
11. [Handle `www`](#11-handle-www)
12. [Turn Maintenance Mode On](#12-turn-maintenance-mode-on)
13. [Turn Maintenance Mode Off](#13-turn-maintenance-mode-off)
14. [Do You Need to Clear Cloudflare Cache?](#14-do-you-need-to-clear-cloudflare-cache)
15. [Troubleshooting](#15-troubleshooting)
16. [Failure Mode: Fail Closed](#16-failure-mode-fail-closed)
17. [Recommended Testing Procedure](#17-recommended-testing-procedure)
18. [Emergency Procedure](#18-emergency-procedure)
19. [Recovery Procedure](#19-recovery-procedure)
20. [Recommended Maintenance Page Design](#20-recommended-maintenance-page-design)
21. [Security and Reliability Notes](#21-security-and-reliability-notes)
22. [Quick Reference](#22-quick-reference)

---

# 1. What This Setup Does

A normal Cloudflare-hosted website typically works like this:

```text
Visitor
   │
   ▼
Cloudflare
   │
   ▼
Your hosting provider
   │
   ▼
Your website
```

If the hosting provider goes down, Cloudflare may be unable to contact the origin and return an error such as:

```text
522 Connection timed out
```

With a Cloudflare Worker used as a temporary maintenance page, the request flow becomes:

```text
Visitor
   │
   ▼
Cloudflare
   │
   ▼
Maintenance Worker
   │
   ▼
Maintenance HTML page
```

The failed hosting provider is not contacted.

This makes the Worker useful for:

- Hosting-provider outages
- Planned server maintenance
- Emergency application maintenance
- Server migrations
- Broken deployments
- Temporary database outages
- Other situations where the normal origin should not receive traffic

---

# 2. How the Traffic Flow Changes

## Normal operation

```text
                    ┌──────────────────┐
                    │     Visitor      │
                    └────────┬─────────┘
                             │
                             ▼
                    ┌──────────────────┐
                    │    Cloudflare    │
                    └────────┬─────────┘
                             │
                             ▼
                    ┌──────────────────┐
                    │ Normal DNS /     │
                    │ Origin Server    │
                    └────────┬─────────┘
                             │
                             ▼
                    ┌──────────────────┐
                    │     Website      │
                    └──────────────────┘
```

## Emergency maintenance mode

```text
                    ┌──────────────────┐
                    │     Visitor      │
                    └────────┬─────────┘
                             │
                             ▼
                    ┌──────────────────┐
                    │    Cloudflare    │
                    └────────┬─────────┘
                             │
                       Worker Route
                             │
                             ▼
                    ┌──────────────────┐
                    │ Maintenance      │
                    │ Worker            │
                    └────────┬─────────┘
                             │
                             ▼
                    ┌──────────────────┐
                    │ Maintenance Page │
                    └──────────────────┘

                    Origin is not contacted.
```

The key advantage is that the **normal DNS record can remain unchanged**.

---

# 3. Prerequisites

Before creating the Worker, confirm:

- The domain is managed by Cloudflare.
- The website already has a DNS record in Cloudflare.
- The DNS record for the hostname you want to protect is **Proxied**.
- You can access **Workers & Pages**.
- You can create and deploy a Worker.

For this example, assume:

```text
Domain:
examplemathsite.com

Normal website:
https://examplemathsite.com

Normal origin:
Your existing hosting provider
```

The actual IP address or CNAME target is intentionally omitted.

### Important

Do **not** change your normal origin DNS target just to create this maintenance system.

The Worker Route is designed to sit in front of your existing origin.

---

# 4. Important: Apex Domain vs. Wildcard Routes

This is one of the easiest places to make a mistake.

Suppose your domain is:

```text
examplemathsite.com
```

These are different hostnames:

```text
examplemathsite.com
www.examplemathsite.com
app.examplemathsite.com
```

A route such as:

```text
*.examplemathsite.com/*
```

is a wildcard route for subdomains.

For an emergency maintenance setup, explicitly create an apex route:

```text
examplemathsite.com/*
```

If your site also uses `www`, create:

```text
www.examplemathsite.com/*
```

### Recommended setup

If visitors can use both addresses:

```text
examplemathsite.com/*
www.examplemathsite.com/*
```

Both should point to the same maintenance Worker.

### Why this matters

If you configure only:

```text
*.examplemathsite.com/*
```

and then visit:

```text
https://examplemathsite.com
```

the apex hostname may not be covered by the route.

This can result in Cloudflare continuing to contact the normal origin, where you may still see a 522 or connection timeout.

---

# 5. Create the Maintenance Worker

Open the Cloudflare Dashboard.

Go to:

**Workers & Pages**

Then:

1. Click **Create application**.
2. Choose **Create Worker**.
3. Give the Worker a descriptive name.

For this example:

```text
examplemathsite-maintenance
```

4. Create/deploy the Worker.

Cloudflare should provide a temporary `workers.dev` address.

It will resemble:

```text
https://examplemathsite-maintenance.<your-account>.workers.dev
```

The exact address is determined by your Cloudflare account.

---

# 6. Edit the Worker

Open:

**Workers & Pages → examplemathsite-maintenance**

Then choose:

**Edit code**

The Cloudflare code editor will appear.

Delete the starter code and replace it with the following.

## Complete maintenance page

```javascript
export default {
  async fetch(request) {
    const html = `<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">

  <title>We'll Be Right Back</title>

  <style>
    * {
      box-sizing: border-box;
    }

    html,
    body {
      margin: 0;
      padding: 0;
      min-height: 100%;
    }

    body {
      font-family:
        -apple-system,
        BlinkMacSystemFont,
        "Segoe UI",
        Roboto,
        Helvetica,
        Arial,
        sans-serif;

      background: linear-gradient(
        135deg,
        #f4f8ff 0%,
        #ffffff 50%,
        #fff8ed 100%
      );

      color: #263238;

      display: flex;
      justify-content: center;
      align-items: center;

      min-height: 100vh;
      padding: 24px;
    }

    .page {
      width: 100%;
      max-width: 680px;
      text-align: center;
    }

    .logo {
      display: inline-flex;
      align-items: center;
      justify-content: center;

      width: 92px;
      height: 92px;

      background: #ffffff;
      border-radius: 24px;

      box-shadow:
        0 8px 30px rgba(0, 0, 0, 0.10);

      margin-bottom: 28px;

      font-size: 48px;
    }

    h1 {
      margin: 0 0 14px;

      font-size: clamp(32px, 6vw, 46px);
      font-weight: 700;

      color: #243447;
    }

    .subtitle {
      margin: 0 auto;
      max-width: 560px;

      font-size: 20px;
      line-height: 1.6;

      color: #52616b;
    }

    .card {
      margin-top: 34px;

      background: #ffffff;

      border-radius: 18px;

      padding: 28px 30px;

      box-shadow:
        0 10px 35px rgba(0, 0, 0, 0.09);
    }

    .card-title {
      margin: 0 0 10px;

      font-size: 21px;
      font-weight: 650;

      color: #243447;
    }

    .card-text {
      margin: 0;

      font-size: 16px;
      line-height: 1.65;

      color: #687780;
    }

    .footer {
      margin-top: 30px;

      font-size: 14px;

      color: #8a969d;
    }

    @media (max-width: 480px) {
      body {
        padding: 18px;
      }

      .logo {
        width: 78px;
        height: 78px;
        font-size: 40px;
        border-radius: 20px;
      }

      .card {
        padding: 24px 20px;
      }

      .subtitle {
        font-size: 18px;
      }
    }
  </style>
</head>

<body>

  <main class="page">

    <div class="logo" aria-hidden="true">
      ☕
    </div>

    <h1>We'll Be Right Back!</h1>

    <p class="subtitle">
      Our website is temporarily unavailable.
      We're working to get everything back up and running.
    </p>

    <div class="card">

      <h2 class="card-title">
        Thanks for your patience!
      </h2>

      <p class="card-text">
        Please check back shortly.
        We expect to have everything back online soon.
      </p>

    </div>

    <div class="footer">
      Example Math Site
    </div>

  </main>

</body>
</html>`;

    return new Response(html, {
      status: 503,
      headers: {
        "Content-Type": "text/html; charset=UTF-8",
        "Cache-Control": "no-store, no-cache, must-revalidate",
        "Pragma": "no-cache"
      }
    });
  }
};
```

---

# 7. Deploy the Worker

After pasting the code:

1. Click **Deploy**.
2. Wait for deployment to complete.

The Worker now contains the maintenance page.

Do not change your production Route yet.

---

# 8. Test the Worker Before Using Your Real Domain

This is an important safety step.

Open the Worker `workers.dev` address:

```text
https://examplemathsite-maintenance.<your-account>.workers.dev
```

You should see:

> **We'll Be Right Back!**

with the maintenance message.

If this page does not work at the `workers.dev` address, do not continue to production routing until the Worker is working.

---

# 9. Verify Your DNS Record

Before creating the Route, verify the production DNS record.

Go to:

**Cloudflare Dashboard → Websites → examplemathsite.com → DNS → Records**

Find the DNS record for the hostname you want to protect.

For the root domain it may look like:

```text
Type: A
Name: @
Content: <your hosting provider IP>
Proxy status: Proxied
```

It could also be a CNAME.

The critical setting is:

```text
Proxy status: Proxied
```

The Cloudflare icon should be:

```text
🟠
```

not:

```text
⚪
```

### Do not change the origin

If the record currently points to your hosting provider, leave the content unchanged.

The maintenance Worker Route operates in front of the origin.

---

# 10. Create the Worker Route

Open:

**Cloudflare Dashboard → Workers & Pages**

Select:

```text
examplemathsite-maintenance
```

Then go to:

**Settings → Domains & Routes**

Choose:

**Add → Route**

Select the zone:

```text
examplemathsite.com
```

For the route, enter:

```text
examplemathsite.com/*
```

Select:

```text
examplemathsite-maintenance
```

Then save/add the route.

The route tells Cloudflare:

> When a request matches this hostname and path, execute this Worker instead of sending the request normally to the origin.

---

# 11. Handle `www`

If your site is accessible through:

```text
www.examplemathsite.com
```

you should also cover that hostname.

Create another route:

```text
www.examplemathsite.com/*
```

and point it to the same maintenance Worker.

A complete configuration might therefore look like:

```text
examplemathsite.com/*
        ↓
examplemathsite-maintenance

www.examplemathsite.com/*
        ↓
examplemathsite-maintenance
```

### Don't automatically add every subdomain

If your domain has:

```text
api.examplemathsite.com
admin.examplemathsite.com
mail.examplemathsite.com
```

do not blindly route those through the maintenance Worker.

Only route hostnames that should display the maintenance page.

---

# 12. Turn Maintenance Mode On

Once everything has been tested, turning maintenance mode on is simple.

Create/enable the production Worker Route:

```text
examplemathsite.com/*
```

and, if appropriate:

```text
www.examplemathsite.com/*
```

The traffic flow becomes:

```text
Visitor
   ↓
Cloudflare
   ↓
Worker Route
   ↓
Maintenance Worker
   ↓
503 maintenance page
```

The normal hosting provider does not need to be available.

---

# 13. Turn Maintenance Mode Off

When your hosting provider is working again:

1. Go to **Workers & Pages**.
2. Open:
   `examplemathsite-maintenance`
3. Go to:
   **Settings → Domains & Routes**
4. Find:
   ```text
   examplemathsite.com/*
   ```
5. Remove/delete the temporary Route.
6. If you created a `www` route, remove that too.

The Worker itself can remain deployed.

The normal traffic flow is restored:

```text
Visitor
   ↓
Cloudflare
   ↓
Normal DNS
   ↓
Hosting provider
   ↓
Website
```

---

# 14. Do You Need to Clear Cloudflare Cache?

Usually, **no**.

The Worker Route determines that the request should be handled by the Worker. The Worker then generates the maintenance response itself.

The Worker in this manual also returns:

```text
Cache-Control: no-store, no-cache, must-revalidate
```

so the maintenance response is explicitly intended not to be stored.

If you make a route change and don't immediately see the expected result:

1. Wait a short time for the configuration change to propagate.
2. Open a private/incognito browser window.
3. Try another browser.
4. Try another device/network.
5. Confirm the DNS record is orange-clouded.
6. Confirm the hostname exactly matches the Worker Route.
7. Confirm the route points to the correct Worker.

A cache purge should not be your first troubleshooting step for a Worker routing problem.

---

# 15. Troubleshooting

## Symptom: The `workers.dev` URL works, but the real domain still shows 522

This usually means the Worker itself is fine and the problem is routing.

Check these in order:

### 1. DNS is proxied

Go to:

**Websites → examplemathsite.com → DNS → Records**

The relevant record must be:

```text
🟠 Proxied
```

not:

```text
⚪ DNS only
```

### 2. The apex route exists

Make sure you have:

```text
examplemathsite.com/*
```

if you're visiting:

```text
https://examplemathsite.com
```

Do not assume that:

```text
*.examplemathsite.com/*
```

covers the apex.

### 3. The Worker is attached to the route

The route should point to:

```text
examplemathsite-maintenance
```

### 4. You're testing the hostname that the route covers

These are different:

```text
examplemathsite.com
www.examplemathsite.com
app.examplemathsite.com
```

### 5. Check DNS resolution

On Windows PowerShell:

```powershell
nslookup examplemathsite.com
```

With Cloudflare proxying enabled, the public DNS result should normally resolve to Cloudflare IP addresses rather than directly exposing your origin IP.

---

## Symptom: `ERR_CONNECTION_TIMED_OUT`

If the Worker `workers.dev` address works but the production domain times out:

Check:

```text
DNS → Proxied
```

and:

```text
Worker → Settings → Domains & Routes
```

The most common causes are:

- DNS is still DNS-only.
- The Route doesn't match the hostname.
- Only a wildcard subdomain route was created.
- The apex route is missing.
- The Route is attached to the wrong Worker.
- The DNS record being used by the hostname isn't the record you made proxied.

---

## Symptom: The maintenance Worker isn't reached at all

Check the exact hostname.

For example, if your browser is visiting:

```text
www.examplemathsite.com
```

but the only route is:

```text
examplemathsite.com/*
```

you may need:

```text
www.examplemathsite.com/*
```

as well.

---

# 16. Failure Mode: Fail Closed

Some Cloudflare Worker configurations expose a failure-mode option.

If you see:

```text
Fail closed (block)
```

this can be appropriate for an emergency maintenance Worker.

The idea is:

```text
Worker works
    ↓
Show maintenance page

Worker fails
    ↓
Block rather than fall through to the broken origin
```

However, failure mode does **not** fix an incorrect Route.

If Cloudflare never matches the Route, the Worker isn't being executed in the first place.

Therefore, troubleshoot in this order:

```text
DNS proxy
   ↓
Hostname
   ↓
Route pattern
   ↓
Worker association
   ↓
Worker code
   ↓
Failure mode
```

---

# 17. Recommended Testing Procedure

It is worth testing this setup before an actual outage.

## Test 1 — Worker

Open:

```text
https://examplemathsite-maintenance.<your-account>.workers.dev
```

Expected:

```text
Maintenance page
```

## Test 2 — Production domain

Add the production route:

```text
examplemathsite.com/*
```

Open:

```text
https://examplemathsite.com
```

Expected:

```text
Maintenance page
```

## Test 3 — `www`

If applicable:

```text
www.examplemathsite.com/*
```

Open:

```text
https://www.examplemathsite.com
```

Expected:

```text
Maintenance page
```

## Test 4 — Remove the Route

Remove the maintenance route.

Open the production website again.

Expected:

```text
Normal website
```

If all four tests work, you have verified the complete emergency switch.

---

# 18. Emergency Procedure

When your hosting provider goes down:

### Step 1 — Confirm the problem

If visitors receive something like:

```text
522 Connection timed out
```

confirm that the origin is actually unavailable.

### Step 2 — Test the Worker

Open:

```text
https://examplemathsite-maintenance.<your-account>.workers.dev
```

Make sure the maintenance page is working.

### Step 3 — Add the Route

Go to:

**Workers & Pages → examplemathsite-maintenance → Settings → Domains & Routes**

Add:

```text
examplemathsite.com/*
```

If required, also add:

```text
www.examplemathsite.com/*
```

### Step 4 — Test

Open:

```text
https://examplemathsite.com
```

Expected:

```text
Maintenance page
```

The origin can remain completely offline.

---

# 19. Recovery Procedure

When the hosting provider reports that the problem is fixed:

### Step 1

Verify the normal site is working.

### Step 2

Remove the maintenance Worker Route.

Remove:

```text
examplemathsite.com/*
```

and, if applicable:

```text
www.examplemathsite.com/*
```

### Step 3

Test the site again.

Use a private/incognito window if necessary.

### Step 4

Confirm the normal website is back.

### Step 5

Leave the Worker deployed.

Keeping the Worker available means it is ready for the next emergency.

---

# 20. Recommended Maintenance Page Design

A maintenance page should ideally be **self-contained**.

Do not depend on resources such as:

```html
<link href="https://examplemathsite.com/styles.css">
<script src="https://examplemathsite.com/app.js"></script>
<img src="https://examplemathsite.com/logo.png">
```

Those resources may also be hosted on the failed origin.

Instead, use:

- Inline CSS
- Inline HTML
- Simple system fonts
- Inline SVG if a logo is needed
- Minimal JavaScript, preferably none

The maintenance page should be able to work even if the entire normal website is unavailable.

---

## Suggested content

A good maintenance page should say:

### What happened

Keep it simple:

> We're temporarily unavailable.

### What the visitor should do

For example:

> Please check back shortly.

### Reassurance

If appropriate:

> Your information and account data are safe.

Only make reassurance claims that are actually true for your application.

### Avoid unnecessary technical details

Visitors generally don't need to see:

```text
HTTP 522
Origin connection timeout
Cloudflare error
DNS failure
```

Those details are useful for administrators, not ordinary visitors.

---

# 21. Security and Reliability Notes

## Don't expose your origin unnecessarily

If Cloudflare is normally protecting the origin, avoid publishing the origin IP address in the maintenance page or documentation.

## Don't change DNS unnecessarily

The maintenance Worker does not require you to replace the existing origin DNS target.

Keep:

```text
DNS → Normal hosting provider
```

and temporarily change routing:

```text
Cloudflare → Maintenance Worker
```

## Keep the Worker simple

The maintenance Worker should have as few moving parts as possible.

A simple static HTML response is preferable to:

- Database calls
- API calls
- External JavaScript
- External CSS
- Authentication
- Application-specific services

The purpose is to work when everything else is broken.

---

# 22. Quick Reference

## Create Worker

```text
Cloudflare
  ↓
Workers & Pages
  ↓
Create application
  ↓
Create Worker
  ↓
examplemathsite-maintenance
```

## Edit Worker

```text
Worker
  ↓
Edit code
  ↓
Paste maintenance Worker
  ↓
Deploy
```

## Test

```text
<worker>.workers.dev
```

## DNS

The production hostname must have a Cloudflare DNS record that is:

```text
🟠 Proxied
```

## Enable maintenance

Add:

```text
examplemathsite.com/*
```

to the maintenance Worker.

If needed, also add:

```text
www.examplemathsite.com/*
```

## Disable maintenance

Remove the temporary Worker Route.

## Do not change

```text
Normal origin IP
Normal CNAME
Normal hosting configuration
```

---

# Final Architecture

## Normal

```text
                    VISITOR
                       │
                       ▼
                 ┌───────────┐
                 │ Cloudflare│
                 └─────┬─────┘
                       │
                       ▼
                 ┌───────────┐
                 │   DNS /   │
                 │   Origin  │
                 └─────┬─────┘
                       │
                       ▼
                 ┌───────────┐
                 │  Website  │
                 └───────────┘
```

## Emergency

```text
                    VISITOR
                       │
                       ▼
                 ┌───────────┐
                 │ Cloudflare│
                 └─────┬─────┘
                       │
                 Worker Route
                       │
                       ▼
              ┌─────────────────┐
              │ Maintenance     │
              │ Worker          │
              └────────┬────────┘
                       │
                       ▼
              ┌─────────────────┐
              │ Maintenance     │
              │ HTML / 503      │
              └─────────────────┘

              Origin is bypassed.
```

---

## The emergency switch in one sentence

> **Add the proxied hostname's Worker Route when the origin is down; remove that Route when the origin is healthy again.**

The normal DNS and hosting configuration can remain unchanged throughout.
