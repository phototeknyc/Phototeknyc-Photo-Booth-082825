# CloudFront Gallery Setup (S3 + Custom Domain)

Use this guide to serve event galleries at your own domain (e.g., `https://photos.phototeknyc.com`) with fast, secure delivery and clean URLs.

## Goals
- Clean, stable links (no AWS credentials visible in the browser)
- Fast global delivery via CDN caching
- Option to keep your S3 bucket private

## Prerequisites
- S3 bucket with gallery under `events/*`
- Access to your DNS provider where `phototeknyc.com` is hosted
- AWS account with permissions to manage CloudFront, ACM, and S3

---

## Option A (Recommended): CloudFront + Private S3 via OAC
This keeps S3 private. CloudFront fetches objects using Origin Access Control (OAC).

1) Create/Verify ACM Certificate (us-east-1)
- Service: AWS Certificate Manager (ACM) in N. Virginia (us-east-1 is required for CloudFront)
- Request public certificate for `photos.phototeknyc.com`
- Validate ownership by adding the DNS CNAMEs at your DNS provider
- Wait until the cert status is Issued

2) Create CloudFront Distribution
- Origin: your S3 bucket (S3 REST endpoint, not website endpoint)
- Origin access: Create an OAC (Origin access control) and attach to this origin
- Alternate domain name (CNAME): `photos.phototeknyc.com`
- TLS certificate: Select your ACM certificate
- Cache/Behavior:
  - Allowed methods: GET, HEAD
  - Viewer protocol: Redirect HTTP to HTTPS
  - Cache policy: CachingOptimized or custom (see Cache-Control below)
  - Compression: Enable, HTTP/2 + HTTP/3: Enable
  - Optional: Response Headers Policy to add CORS headers for Download-All (JSZip)

3) Allow CloudFront to Read S3 via OAC
Update your bucket policy for read access to `events/*` via the OAC. Use the policy CloudFront suggests, or adapt this template:

```
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Sid": "AllowCloudFrontReadEvents",
      "Effect": "Allow",
      "Principal": { "Service": "cloudfront.amazonaws.com" },
      "Action": ["s3:GetObject"],
      "Resource": "arn:aws:s3:::YOUR_BUCKET_NAME/events/*",
      "Condition": {
        "StringEquals": {
          "AWS:SourceArn": "arn:aws:cloudfront::YOUR_ACCOUNT_ID:distribution/YOUR_DISTRIBUTION_ID"
        }
      }
    }
  ]
}
```

4) DNS: Point your domain to CloudFront
- At your DNS provider (where `phototeknyc.com` is hosted), create a CNAME:
  - Name/Host: `photos`
  - Value/Target: your CloudFront domain (e.g., `dxxxxxxxxxxxx.cloudfront.net`)
  - TTL: 300–3600 seconds

5) App Configuration
- In the app Settings (Cloud):
  - `GALLERY_BASE_URL`: `https://photos.phototeknyc.com`
  - Turn OFF “Use Pre‑Signed URLs (private bucket)” — CloudFront handles access
- Regenerate the event gallery so pages reference your new domain

6) Optional: CORS for Download‑All
- If using JSZip + fetch to zip downloads, allow CORS either via:
  - CloudFront Response Headers Policy, or
  - S3 CORS (origin)

Example S3 CORS (adjust origin):
```
[
  {
    "AllowedOrigins": ["https://photos.phototeknyc.com"],
    "AllowedMethods": ["GET"],
    "AllowedHeaders": ["*"],
    "ExposeHeaders": ["Content-Length"],
    "MaxAgeSeconds": 3000
  }
]
```

7) Cache-Control (Performance)
- Images: `Cache-Control: public, max-age=31536000, immutable`
- HTML: `Cache-Control: public, max-age=60` (or rely on CloudFront with a low TTL for HTML)
- After regenerating a gallery, either:
  - Invalidate `events/<event>/index.html` in CloudFront, or
  - Use short HTML TTL so updates appear quickly

Example invalidation:
```
aws cloudfront create-invalidation \
  --distribution-id YOUR_DISTRIBUTION_ID \
  --paths "/events/EVENT_FOLDER/index.html"
```

---

## Option B: S3 Website (Public)
Simpler but requires public read and typically needs CloudFront for HTTPS on your domain.

1) Enable static website hosting on the bucket
2) Make `events/*` publicly readable (restrict scope appropriately)
```
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Sid": "PublicReadEvents",
      "Effect": "Allow",
      "Principal": "*",
      "Action": ["s3:GetObject"],
      "Resource": "arn:aws:s3:::YOUR_BUCKET_NAME/events/*"
    }
  ]
}
```
3) DNS CNAME `photos.phototeknyc.com` → S3 website endpoint (e.g., `YOUR_BUCKET_NAME.s3-website-us-east-1.amazonaws.com`)
4) App Settings: `GALLERY_BASE_URL = https://photos.phototeknyc.com`, Pre‑Signed URLs OFF
5) Note: Custom HTTPS with S3 website requires CloudFront — prefer Option A

---

## App Integration Checklist
- Set `GALLERY_BASE_URL` to your CloudFront domain
- Turn OFF “Use Pre‑Signed URLs (private bucket)”
- Regenerate the event gallery
- Verify:
  - Clean URLs in the browser (no AWSAccessKeyId/Signature)
  - Images load quickly from the CDN
  - Download‑All works (CORS OK)

---

## Troubleshooting
- 403 from CloudFront: Check bucket policy for OAC, OAC attached to origin, and distribution deployed
- 404 from CloudFront: Key names differ from expected; verify event/gallery paths
- ACM not Issued: Ensure DNS CNAMEs at your DNS host match ACM validation records
- DNS not resolving: CNAME not published at the correct DNS provider or still propagating
- Slow HTML updates: Invalidate the HTML path or lower HTML TTL

---

## References
- CloudFront OAC docs: https://docs.aws.amazon.com/AmazonCloudFront/latest/DeveloperGuide/private-content-restricting-access-to-s3.html
- ACM for CloudFront: https://docs.aws.amazon.com/acm/latest/userguide/acm-services.html#acm-cloudfront
- S3 CORS: https://docs.aws.amazon.com/AmazonS3/latest/userguide/cors.html

