"""Вызов Unity только через штатный worktree-прокси; полный ответ остаётся в reports/."""
import argparse
import asyncio
import json
import sys
from pathlib import Path

from mcp import ClientSession, StdioServerParameters
from mcp.client.stdio import stdio_client

ROOT = Path(__file__).resolve().parents[3]

async def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--tool', required=True)
    parser.add_argument('--args', required=True)
    parser.add_argument('--out', required=True)
    options = parser.parse_args()
    arguments = json.loads(Path(options.args).read_text(encoding='utf-8-sig'))
    server = StdioServerParameters(command='uv',
                                  args=['run', '--quiet', str(ROOT / 'Tools/agents/unity_mcp_proxy.py')], cwd=str(ROOT))
    print(json.dumps({'phase': 'starting-proxy', 'root': str(ROOT)}), flush=True)
    async with stdio_client(server) as (reader, writer):
        async with ClientSession(reader, writer) as session:
            print(json.dumps({'phase': 'initializing'}), flush=True)
            async with asyncio.timeout(45):
                await session.initialize()
            print(json.dumps({'phase': 'calling', 'tool': options.tool}), flush=True)
            async with asyncio.timeout(180):
                result = await session.call_tool(options.tool, arguments)
            raw = result.model_dump(mode='json')
            output = Path(options.out)
            output.parent.mkdir(parents=True, exist_ok=True)
            output.write_text(json.dumps(raw, ensure_ascii=False, indent=2), encoding='utf-8')
            data = raw.get('structuredContent')
            if data is None:
                data = next((json.loads(c['text']) for c in raw.get('content', []) if c.get('type') == 'text'), {})
            while isinstance(data, dict) and isinstance(data.get('result'), dict) and 'success' in data['result']:
                data = data['result']
            payload = data.get('data') or data.get('result') or {}
            if not isinstance(payload, dict):
                payload = {'value': str(payload)[:200]}
            print(json.dumps({'success': data.get('success'), 'error': data.get('error'),
                              'message': data.get('message'), 'job_id': payload.get('job_id'),
                              'status': payload.get('status'), 'summary': (payload.get('result') or {}).get('summary'),
                              'report': str(output)}, ensure_ascii=False))

if __name__ == '__main__':
    sys.stdout.reconfigure(encoding='utf-8')
    asyncio.run(main())
