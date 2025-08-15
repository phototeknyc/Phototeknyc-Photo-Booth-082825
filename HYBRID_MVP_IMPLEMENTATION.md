# Hybrid MVP Implementation Plan

## 🎯 Hybrid Approach: Ultra-Lean Start
**Goal: Launch with <$10/month infrastructure for first 5 clients**

We'll use a hybrid of free services and minimal paid infrastructure to validate the business model before scaling up.

---

## 🏗️ Architecture Overview

```
Photobooth App (WPF)
    ↓
Local Queue (SQLite)
    ↓
Direct S3 Upload (Presigned URLs)
    ↓
Supabase (Free Tier)
    ├── Authentication
    ├── Database (Postgres)
    ├── Realtime subscriptions
    └── Edge Functions
    
Stripe Payment Links → Webhook → License Key Email
```

---

## 📦 Week 1: Core Infrastructure Setup

### Day 1-2: Backend Setup
```bash
# Total Monthly Cost: $0 (all free tier)
```

#### 1. Supabase Setup (Free: 500MB database, 2GB storage, 50K MAU)
- [ ] Create Supabase project
- [ ] Set up tables:
  ```sql
  -- Subscriptions table
  CREATE TABLE subscriptions (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    license_key VARCHAR(32) UNIQUE NOT NULL,
    email VARCHAR(255) NOT NULL,
    tier VARCHAR(20) DEFAULT 'basic',
    status VARCHAR(20) DEFAULT 'active',
    photos_used INTEGER DEFAULT 0,
    photos_limit INTEGER DEFAULT 500,
    storage_used_mb DECIMAL(10,2) DEFAULT 0,
    created_at TIMESTAMP DEFAULT NOW(),
    expires_at TIMESTAMP,
    stripe_customer_id VARCHAR(255),
    stripe_subscription_id VARCHAR(255)
  );

  -- Sessions table
  CREATE TABLE sessions (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    subscription_id UUID REFERENCES subscriptions(id),
    session_id VARCHAR(255) NOT NULL,
    photo_count INTEGER DEFAULT 0,
    storage_mb DECIMAL(10,2) DEFAULT 0,
    metadata JSONB,
    created_at TIMESTAMP DEFAULT NOW(),
    completed_at TIMESTAMP
  );

  -- Photos table (metadata only, files in S3)
  CREATE TABLE photos (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    session_id UUID REFERENCES sessions(id),
    filename VARCHAR(255),
    s3_key VARCHAR(500),
    size_bytes INTEGER,
    width INTEGER,
    height INTEGER,
    uploaded_at TIMESTAMP DEFAULT NOW(),
    share_url TEXT,
    expires_at TIMESTAMP
  );

  -- Analytics events table
  CREATE TABLE analytics (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    subscription_id UUID REFERENCES subscriptions(id),
    event_type VARCHAR(50),
    properties JSONB,
    created_at TIMESTAMP DEFAULT NOW()
  );
  ```

#### 2. AWS S3 Setup (Pay as you go: ~$2-5/month for first clients)
- [ ] Create S3 bucket: `photobooth-uploads-prod`
- [ ] Configure CORS for direct upload:
  ```json
  {
    "CORSRules": [{
      "AllowedHeaders": ["*"],
      "AllowedMethods": ["PUT", "POST", "GET"],
      "AllowedOrigins": ["*"],
      "ExposeHeaders": ["ETag"],
      "MaxAgeSeconds": 3000
    }]
  }
  ```
- [ ] Set up lifecycle rules:
  - Move to IA after 30 days
  - Move to Glacier after 90 days

#### 3. Netlify Setup (Free tier: 100GB bandwidth, 300 build minutes)
- [ ] Create Netlify account
- [ ] Deploy edge functions:

**`netlify/functions/validate-license.js`**
```javascript
const { createClient } = require('@supabase/supabase-js');
const supabase = createClient(process.env.SUPABASE_URL, process.env.SUPABASE_KEY);

exports.handler = async (event) => {
  const { licenseKey } = JSON.parse(event.body);
  
  const { data, error } = await supabase
    .from('subscriptions')
    .select('*')
    .eq('license_key', licenseKey)
    .eq('status', 'active')
    .single();
    
  if (error || !data) {
    return {
      statusCode: 401,
      body: JSON.stringify({ valid: false })
    };
  }
  
  return {
    statusCode: 200,
    body: JSON.stringify({
      valid: true,
      tier: data.tier,
      photosUsed: data.photos_used,
      photosLimit: data.photos_limit,
      expiresAt: data.expires_at
    })
  };
};
```

**`netlify/functions/get-upload-url.js`**
```javascript
const AWS = require('aws-sdk');
const { createClient } = require('@supabase/supabase-js');

const s3 = new AWS.S3({
  accessKeyId: process.env.AWS_ACCESS_KEY_ID,
  secretAccessKey: process.env.AWS_SECRET_ACCESS_KEY,
  region: 'us-east-1'
});

exports.handler = async (event) => {
  const { licenseKey, sessionId, filename, contentType } = JSON.parse(event.body);
  
  // Validate license
  const supabase = createClient(process.env.SUPABASE_URL, process.env.SUPABASE_KEY);
  const { data: subscription } = await supabase
    .from('subscriptions')
    .select('id, photos_used, photos_limit')
    .eq('license_key', licenseKey)
    .single();
    
  if (!subscription || subscription.photos_used >= subscription.photos_limit) {
    return {
      statusCode: 403,
      body: JSON.stringify({ error: 'Photo limit exceeded' })
    };
  }
  
  // Generate presigned URL
  const key = `${subscription.id}/${sessionId}/${Date.now()}-${filename}`;
  const uploadUrl = await s3.getSignedUrlPromise('putObject', {
    Bucket: 'photobooth-uploads-prod',
    Key: key,
    Expires: 3600,
    ContentType: contentType
  });
  
  // Store photo metadata
  await supabase.from('photos').insert({
    session_id: sessionId,
    filename: filename,
    s3_key: key
  });
  
  // Increment photo count
  await supabase.rpc('increment_photo_count', {
    sub_id: subscription.id
  });
  
  return {
    statusCode: 200,
    body: JSON.stringify({ uploadUrl, key })
  };
};
```

### Day 3-4: Payment Integration

#### Stripe Setup (2.9% + $0.30 per transaction)
- [ ] Create products:
  - Basic: $29/month
  - Pro: $79/month
  - Enterprise: $199/month
  
- [ ] Create payment links with metadata
- [ ] Set up webhook endpoint on Netlify

**`netlify/functions/stripe-webhook.js`**
```javascript
const stripe = require('stripe')(process.env.STRIPE_SECRET_KEY);
const { createClient } = require('@supabase/supabase-js');

exports.handler = async (event) => {
  const sig = event.headers['stripe-signature'];
  const webhookSecret = process.env.STRIPE_WEBHOOK_SECRET;
  
  let stripeEvent;
  try {
    stripeEvent = stripe.webhooks.constructEvent(event.body, sig, webhookSecret);
  } catch (err) {
    return { statusCode: 400, body: `Webhook Error: ${err.message}` };
  }
  
  const supabase = createClient(process.env.SUPABASE_URL, process.env.SUPABASE_KEY);
  
  switch (stripeEvent.type) {
    case 'checkout.session.completed':
      const session = stripeEvent.data.object;
      
      // Generate license key
      const licenseKey = generateLicenseKey();
      
      // Create subscription
      await supabase.from('subscriptions').insert({
        license_key: licenseKey,
        email: session.customer_email,
        tier: session.metadata.tier || 'basic',
        stripe_customer_id: session.customer,
        stripe_subscription_id: session.subscription,
        photos_limit: getTierLimit(session.metadata.tier),
        expires_at: new Date(Date.now() + 30 * 24 * 60 * 60 * 1000)
      });
      
      // Send license key email
      await sendLicenseEmail(session.customer_email, licenseKey);
      break;
      
    case 'customer.subscription.deleted':
      // Handle cancellation
      await supabase
        .from('subscriptions')
        .update({ status: 'cancelled' })
        .eq('stripe_subscription_id', stripeEvent.data.object.id);
      break;
  }
  
  return { statusCode: 200, body: 'Success' };
};

function generateLicenseKey() {
  return 'PB-' + Math.random().toString(36).substr(2, 9).toUpperCase();
}

function getTierLimit(tier) {
  const limits = {
    'basic': 500,
    'pro': 2000,
    'enterprise': 10000
  };
  return limits[tier] || 500;
}
```

### Day 5: WPF Application Changes

#### New Service Classes

**`Services/CloudSyncService.cs`**
```csharp
using System;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Photobooth.Services
{
    public class CloudSyncService
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiEndpoint = "https://your-app.netlify.app/.netlify/functions";
        private string _licenseKey;
        private SubscriptionInfo _subscription;

        public CloudSyncService()
        {
            _httpClient = new HttpClient();
            _licenseKey = Properties.Settings.Default.LicenseKey;
        }

        public async Task<bool> ValidateLicenseAsync()
        {
            if (string.IsNullOrEmpty(_licenseKey))
                return false;

            var response = await _httpClient.PostAsJsonAsync(
                $"{_apiEndpoint}/validate-license",
                new { licenseKey = _licenseKey });

            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                _subscription = JsonConvert.DeserializeObject<SubscriptionInfo>(json);
                return _subscription.Valid;
            }

            return false;
        }

        public async Task<string> GetUploadUrlAsync(string sessionId, string filename)
        {
            var response = await _httpClient.PostAsJsonAsync(
                $"{_apiEndpoint}/get-upload-url",
                new 
                { 
                    licenseKey = _licenseKey,
                    sessionId = sessionId,
                    filename = filename,
                    contentType = "image/jpeg"
                });

            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                dynamic result = JsonConvert.DeserializeObject(json);
                return result.uploadUrl;
            }

            throw new Exception("Failed to get upload URL");
        }

        public async Task<bool> UploadPhotoAsync(string filePath, string uploadUrl)
        {
            using (var fileStream = File.OpenRead(filePath))
            {
                var content = new StreamContent(fileStream);
                content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");

                var response = await _httpClient.PutAsync(uploadUrl, content);
                return response.IsSuccessStatusCode;
            }
        }
    }
}
```

**`Services/PhotoUploadQueue.cs`**
```csharp
using System.Data.SQLite;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Photobooth.Services
{
    public class PhotoUploadQueue
    {
        private readonly string _dbPath = "photobooth_queue.db";
        private readonly CloudSyncService _cloudSync;

        public PhotoUploadQueue()
        {
            _cloudSync = new CloudSyncService();
            InitializeDatabase();
        }

        private void InitializeDatabase()
        {
            using (var conn = new SQLiteConnection($"Data Source={_dbPath}"))
            {
                conn.Open();
                var cmd = new SQLiteCommand(@"
                    CREATE TABLE IF NOT EXISTS upload_queue (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        session_id TEXT NOT NULL,
                        file_path TEXT NOT NULL,
                        status TEXT DEFAULT 'pending',
                        retry_count INTEGER DEFAULT 0,
                        created_at DATETIME DEFAULT CURRENT_TIMESTAMP
                    )", conn);
                cmd.ExecuteNonQuery();
            }
        }

        public async Task AddToQueueAsync(string sessionId, string filePath)
        {
            using (var conn = new SQLiteConnection($"Data Source={_dbPath}"))
            {
                conn.Open();
                var cmd = new SQLiteCommand(
                    "INSERT INTO upload_queue (session_id, file_path) VALUES (@session, @path)", 
                    conn);
                cmd.Parameters.AddWithValue("@session", sessionId);
                cmd.Parameters.AddWithValue("@path", filePath);
                cmd.ExecuteNonQuery();
            }
        }

        public async Task ProcessQueueAsync()
        {
            var pending = GetPendingUploads();
            
            foreach (var item in pending)
            {
                try
                {
                    var uploadUrl = await _cloudSync.GetUploadUrlAsync(
                        item.SessionId, 
                        Path.GetFileName(item.FilePath));
                    
                    var success = await _cloudSync.UploadPhotoAsync(
                        item.FilePath, 
                        uploadUrl);
                    
                    if (success)
                    {
                        MarkAsCompleted(item.Id);
                        // Delete local file after successful upload (optional)
                        // File.Delete(item.FilePath);
                    }
                }
                catch (Exception ex)
                {
                    IncrementRetryCount(item.Id);
                }
            }
        }
    }
}
```

---

## 💰 Cost Breakdown for This Hybrid Approach

### Monthly Costs (1-10 clients)
```
Supabase: $0 (free tier)
Netlify: $0 (free tier)
AWS S3: $2-5 (storage + requests)
Stripe: ~$15 (fees on $290 revenue)
Domain: $1 (amortized)
------------------------
Total: $18-21/month

Revenue (10 clients): $290
Gross Profit: $269-272
Margin: 92-94%
```

### When to Scale Up (10+ clients)
```
At 10 clients: Stay on hybrid
At 20 clients: Add CloudFront CDN ($15/month)
At 50 clients: Move to AWS Amplify ($100/month)
At 100 clients: Full architecture ($500/month)
```

---

## 🚀 Launch Checklist

### Week 1 Deliverables
- [ ] Supabase project live
- [ ] S3 bucket configured
- [ ] Netlify functions deployed
- [ ] Stripe products created
- [ ] License key generation working
- [ ] WPF app connecting to cloud

### Week 2 Goals
- [ ] First test upload successful
- [ ] Payment flow tested
- [ ] Queue processing working
- [ ] First beta customer onboarded
- [ ] Basic monitoring in place

---

## 📊 Success Metrics

### MVP Success (Month 1)
- 3+ paying customers
- <$25/month infrastructure
- 99% upload success rate
- <5 second upload time per photo

### Validation (Month 2)
- 10+ paying customers
- 90%+ gross margins
- <1% churn rate
- Positive customer feedback

---

## 🔧 Quick Start Commands

```bash
# Install Netlify CLI
npm install -g netlify-cli

# Create new Netlify site
netlify init

# Set environment variables
netlify env:set SUPABASE_URL your-project-url
netlify env:set SUPABASE_KEY your-anon-key
netlify env:set AWS_ACCESS_KEY_ID your-key
netlify env:set AWS_SECRET_ACCESS_KEY your-secret
netlify env:set STRIPE_SECRET_KEY your-stripe-key

# Deploy functions
netlify deploy --prod

# Test locally
netlify dev
```

---

## 🎯 Next Steps After MVP

1. **Add Analytics Dashboard** (Week 3)
   - Simple React app on Netlify
   - Charts.js for visualization
   - Supabase real-time subscriptions

2. **Guest Data Collection** (Week 4)
   - Email/phone capture
   - Automated sharing
   - GDPR consent

3. **Template Marketplace** (Month 2)
   - Upload/download templates
   - Revenue sharing
   - Version control

---

*This hybrid approach gives us the fastest path to revenue with minimal infrastructure costs while maintaining the ability to scale.*