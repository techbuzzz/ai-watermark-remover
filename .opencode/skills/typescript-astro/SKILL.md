---
name: typescript-astro
description: >
  Use when building with Astro framework: creating components, layouts, pages,
  content collections, integrations; working with Astro islands, SSR/SSG,
  TypeScript in Astro files; optimizing images, SEO, and performance.
license: MIT
compatibility: opencode
metadata:
  stack: astro, typescript, javascript, ssg, ssr
  version: Astro 4+/5+
---

# TypeScript & Astro Expert Skill

You are an expert in Astro framework and modern TypeScript, building fast, SEO-optimized sites.

## When to Use

Activate this skill when the user:
- Builds or maintains an Astro project
- Creates Astro components (`.astro` files)
- Works with content collections in Astro
- Implements SSR or SSG strategies
- Uses Astro integrations (React, Vue, Svelte, Tailwind)
- Writes advanced TypeScript: generics, utility types, decorators
- Optimizes Astro site performance and SEO

## TypeScript Best Practices

### Strict Mode
```typescript
// tsconfig.json — always enable strict
{
  "compilerOptions": {
    "strict": true,
    "noUncheckedIndexedAccess": true,
    "exactOptionalPropertyTypes": true
  }
}
```

### Types vs Interfaces
```typescript
// ✅ Use type aliases for unions, intersections, primitives
type UserId = string & { readonly __brand: 'UserId' };
type Status = 'active' | 'inactive' | 'pending';
type Result<T> = { ok: true; data: T } | { ok: false; error: string };

// ✅ Use interfaces for object shapes (more extendable)
interface User {
  id: UserId;
  name: string;
  email: string;
  status: Status;
}
```

### Utility Types
```typescript
// ✅ Use built-in utility types
type PartialUser = Partial<User>;
type ReadonlyUser = Readonly<User>;
type UserPreview = Pick<User, 'id' | 'name'>;
type CreateUserInput = Omit<User, 'id'>;

// ✅ Conditional types
type NonNullable<T> = T extends null | undefined ? never : T;
type Awaited<T> = T extends Promise<infer R> ? R : T;
```

### Generics
```typescript
// ✅ Constrained generics
function pick<T, K extends keyof T>(obj: T, keys: K[]): Pick<T, K> {
  return keys.reduce((acc, key) => ({ ...acc, [key]: obj[key] }), {} as Pick<T, K>);
}

// ✅ Generic Result type
async function fetchUser(id: string): Promise<Result<User>> {
  try {
    const user = await api.getUser(id);
    return { ok: true, data: user };
  } catch (err) {
    return { ok: false, error: err instanceof Error ? err.message : 'Unknown error' };
  }
}
```

## Astro Component Patterns

### Basic Component
```astro
---
// src/components/Card.astro
interface Props {
  title: string;
  description: string;
  href?: string;
  image?: ImageMetadata;
}

const { title, description, href, image } = Astro.props;
---

<article class="rounded-lg border p-6">
  {image && <Image src={image} alt={title} width={400} height={300} />}
  <h2>{title}</h2>
  <p>{description}</p>
  {href && <a href={href}>Read more →</a>}
</article>
```

### Content Collections (Astro 5+)
```typescript
// src/content/config.ts
import { defineCollection, z } from 'astro:content';

const blog = defineCollection({
  type: 'content',
  schema: ({ image }) => z.object({
    title: z.string().max(80),
    description: z.string().max(160),
    pubDate: z.date(),
    heroImage: image().optional(),
    tags: z.array(z.string()).default([]),
    draft: z.boolean().default(false),
  }),
});

export const collections = { blog };
```

```astro
---
// src/pages/blog/[...slug].astro
import { getCollection, type CollectionEntry } from 'astro:content';

export async function getStaticPaths() {
  const posts = await getCollection('blog', ({ data }) => !data.draft);
  return posts.map(post => ({
    params: { slug: post.slug },
    props: { post },
  }));
}

interface Props { post: CollectionEntry<'blog'> }
const { post } = Astro.props;
const { Content } = await post.render();
---

<Layout title={post.data.title}>
  <Content />
</Layout>
```

### Astro Islands (Partial Hydration)
```astro
---
import Counter from '../components/Counter.tsx';  // React component
import Map from '../components/Map.vue';           // Vue component
---

<!-- Only hydrate on visible -->
<Counter client:visible />

<!-- Hydrate immediately (critical UI) -->
<Map client:load />

<!-- Hydrate when idle -->
<Counter client:idle />

<!-- Never hydrate (static HTML only) -->
<Counter />
```

### SSR with Astro
```astro
---
// src/pages/dashboard.astro
export const prerender = false;  // Enable SSR for this page

const session = await getSession(Astro.cookies);
if (!session) return Astro.redirect('/login');

const data = await fetchDashboardData(session.userId);
---

<DashboardLayout>
  <DataGrid data={data} />
</DashboardLayout>
```

## SEO & Performance

```astro
---
// src/components/SEO.astro
interface Props {
  title: string;
  description: string;
  image?: string;
  canonical?: string;
}
const { title, description, image, canonical } = Astro.props;
const siteUrl = import.meta.env.SITE;
---

<meta name="description" content={description} />
<meta property="og:title" content={title} />
<meta property="og:description" content={description} />
{image && <meta property="og:image" content={new URL(image, siteUrl)} />}
<link rel="canonical" href={canonical ?? Astro.url} />
```

## Quality Checklist

Before completing Astro/TypeScript work, verify:
- [ ] TypeScript strict mode enabled, no `any` types
- [ ] All component Props interfaces defined
- [ ] Images use `<Image />` component for optimization
- [ ] Content collections have proper Zod schema validation
- [ ] SEO meta tags present on all pages
- [ ] No layout shift (CLS) from dynamic content
- [ ] `client:*` directives only where interactivity is needed
- [ ] Build succeeds: `astro build`
- [ ] Lighthouse score > 90 for performance
