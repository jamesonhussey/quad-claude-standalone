#!/usr/bin/env node
// monday-tasks.mjs — CLI fetch backend for QuadClaude's Monday panel.
//
// Fetches one board group's items from the monday.com GraphQL API and prints a
// normalized JSON array of tasks to stdout. This is the "CLI" half of the
// panel's side-by-side comparison (the other half is the in-process C# client).
//
// Usage:
//   MONDAY_API_TOKEN=xxx node monday-tasks.mjs \
//     --board <id> --group <group> --host yourco.monday.com
//
// Output (stdout): [{ id, name, status, statusColor, priority, branch, owner, group, url }, ...]
// Errors go to stderr with a non-zero exit code.

const args = process.argv.slice(2);
function arg(flag, fallback) {
  const i = args.indexOf(flag);
  return i >= 0 && i + 1 < args.length ? args[i + 1] : fallback;
}

const token = arg('--token', process.env.MONDAY_API_TOKEN);
const boardId = arg('--board', '');
const groupId = arg('--group', '');
const host = arg('--host', '');
const itemId = arg('--item', null); // when set: fetch that item's detail instead of the list

if (!token) {
  console.error('No monday API token. Pass --token or set MONDAY_API_TOKEN.');
  process.exit(2);
}

if (!boardId || !groupId || !host) {
  console.error('Monday not configured — pass --board/--group/--host or set them via QuadClaude setup.');
  process.exit(2);
}

// Same query shape as GraphQlTaskSource.Query (kept in sync intentionally).
const query = `query ($boardId: ID!, $groupId: String!) {
  boards(ids: [$boardId]) {
    groups(ids: [$groupId]) {
      id title
      items_page(limit: 100) {
        items {
          id name
          column_values(ids: ["status","color_mm2c4cj6","text_mm1t4h16","person"]) { id text }
        }
      }
    }
  }
}`;

// Status / priority label → hex, mirrors MondayStatusColors.cs.
const STATUS_COLORS = {
  'Working on it': '#fdab3d', 'Done': '#00c875', 'Done for Sprint': '#037f4c',
  'Stuck/Waiting': '#df2f4a', 'Self-Assigned': '#225091', 'Approved for work': '#74afcc',
  'PR Submitted': '#9d50dd', 'Needs Adjustment': '#ff6d3b', 'Approved for Merging': '#7e3b8a',
  'Staging Merged': '#9cd326', 'Staging Testing': '#ff007f', 'Declined': '#7f5347',
  'Reviewed': '#401694', 'Rough Local': '#66ccff', 'Local Tested': '#579bfc',
  'Test me: Staging': '#faa1f1', 'Staging Tested': '#007eb5', 'Prod Merged': '#5559df',
  'Prod Tested': '#784bd1', 'Prod Built': '#bda8f9', 'Test me: Prod': '#9d99b9',
  'Documentation': '#563e3e', 'Needs Review': '#216edf', 'Rollout': '#333333',
  'Assign Effort': '#ffadad',
};
const blank = (s) => (s && s.trim() ? s.trim() : null);

// One item's body — description blocks + recent updates. Mirrors GraphQlTaskSource.DetailQuery.
const detailQuery = `query ($itemId: [ID!]) {
  items(ids: $itemId) {
    id
    description { blocks(limit: 60) { type content } }
    updates(limit: 5) { text_body created_at creator { name } }
  }
}`;

// Extract text from a Quill-style delta block (content may be object or JSON string).
function blockText(content) {
  let c = content;
  if (typeof c === 'string') { try { c = JSON.parse(c); } catch { return ''; } }
  if (c && Array.isArray(c.deltaFormat))
    return c.deltaFormat.map((o) => (typeof o.insert === 'string' ? o.insert : '')).join('');
  return '';
}

async function post(query, variables) {
  const resp = await fetch('https://api.monday.com/v2', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', 'Authorization': token, 'API-Version': '2025-07' },
    body: JSON.stringify({ query, variables }),
  });
  const body = await resp.json();
  if (!resp.ok) throw new Error(`HTTP ${resp.status}: ${JSON.stringify(body)}`);
  if (body.errors) throw new Error(`GraphQL error: ${JSON.stringify(body.errors)}`);
  return body;
}

try {
  if (itemId) {
    const body = await post(detailQuery, { itemId: [String(itemId)] });
    const item = (body.data?.items ?? [])[0] ?? {};
    const description =
      (item.description?.blocks ?? []).map((b) => blockText(b.content)).filter(Boolean).join('\n') || null;
    const updates = (item.updates ?? [])
      .filter((u) => u.text_body && u.text_body.trim())
      .map((u) => ({ author: u.creator?.name ?? null, date: u.created_at ?? null, text: u.text_body.trim() }));
    process.stdout.write(JSON.stringify({ description, updates }));
    process.exit(0);
  }

  const body = await post(query, { boardId: String(boardId), groupId });

  const tasks = [];
  const boards = body.data?.boards ?? [];
  for (const group of boards[0]?.groups ?? []) {
    for (const item of group.items_page?.items ?? []) {
      const cols = Object.fromEntries((item.column_values ?? []).map((c) => [c.id, blank(c.text)]));
      const status = cols['status'] ?? null;
      tasks.push({
        id: item.id,
        name: item.name,
        status,
        statusColor: STATUS_COLORS[status] ?? '#6A6A7A',
        priority: cols['color_mm2c4cj6'] ?? null,
        branch: cols['text_mm1t4h16'] ?? null,
        owner: cols['person'] ?? null,
        group: group.title ?? null,
        url: `https://${host}/boards/${boardId}/pulses/${item.id}`,
      });
    }
  }

  process.stdout.write(JSON.stringify(tasks));
} catch (err) {
  console.error(String(err?.message ?? err));
  process.exit(1);
}
