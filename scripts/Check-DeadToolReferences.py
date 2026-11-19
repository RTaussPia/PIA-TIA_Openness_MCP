"""Check that MCP tool names cited in descriptions and instructions are registered."""
import re
import io
import os
import sys
import collections

ROOT = 'tools/tiaportal-mcp/src/TiaMcpServer'

ALLOWED = {
    'GetService': 'Openness IEngineeringObject.GetService<T>()',
    'GetAttribute': 'Openness IEngineeringObject.GetAttribute()',
    'GetAttributeInfos': 'Openness IEngineeringObject.GetAttributeInfos()',
    'GetSupportedFileFormats': 'Openness Workspace.GetSupportedFileFormats()',
    'ConnectObject': 'Openness Workspace.ConnectObject()',
    'ImportDocumentOptions': 'Openness enum for importOption',
    'DownloadProvider': 'Openness DownloadProvider type',
    'AddSignalBoard': 'TIA operation name',
    'BuildPlc': 'Tool family prefix',
    'CompileError': 'Error code',
    'RunOut': 'HMI tag created by EnsureStartStopUnifiedHmi',
    'DeleteDb': 'Unavailable alias for DeletePlcBlock',
    'DeleteGlobalDb': 'Unavailable alias for DeletePlcBlock',
    'DeleteFunctionBlock': 'Unavailable alias for DeletePlcBlock',
    'ImportInstanceTexts': 'Openness API documented as unavailable',
    'GetHardDeniedReflectionReason': 'Internal reflection policy method',
    'DescribeGuardResult': 'Internal self-test helper',
    'WriteValue': 'OPC UA write service cited by a self-test',
    'ReadLineAsync': '.NET StreamReader method',
    'CompileUnit': 'SimaticML element',
    'SetPoint': 'Example PLC tag name',
    'ApplyConfiguration': 'Openness ConnectionConfiguration.ApplyConfiguration()',
    'GenerateBlocks': 'Openness PlcExternalSource.GenerateBlocks()',
    'GenerateBlocksFromSource': 'Openness PlcExternalSource.GenerateBlocksFromSource()',
    'BuildExternalSourceImportArguments': 'Internal external-source import helper',
}

EXTRA_SOURCES = [
    os.path.join(ROOT, 'ModelContextProtocol', 'McpGuides.cs'),
    os.path.join(ROOT, 'ModelContextProtocol', 'McpServer.cs'),
]
SKILL_MD = 'tools/tiaportal-mcp/skill/SKILL.md'
ANY_STR = re.compile(r'@"(?:[^"]|"")*"|"(?:[^"\\\n]|\\.)*"', re.S)
TICK = re.compile(r'`([A-Z][A-Za-z0-9]{3,})(?:\(|`)')

VERB = re.compile(
    r'^(Get|Set|Add|Import|Export|Create|Delete|Compile|Download|Sync|Analyze'
    r'|Build|Write|Read|Ensure|Find|List|Describe|Invoke|Generate|Apply|Bind'
    r'|Move|Rename|Save|Open|Close|Connect|Run|Check|Validate|Preflight|Scaffold|Attach)[A-Z]')
STR = r'"[^"]*"'
LIT = re.compile(r'Description\(\s*((?:@?' + STR + r'\s*\+?\s*)+)\)', re.S)
PIECE = re.compile(STR)
TOK = re.compile(r'\b([A-Z][A-Za-z0-9]{3,})\b')


def load(root):
    src = {}
    for dp, _, fs in os.walk(root):
        for f in fs:
            if f.endswith('.cs'):
                p = os.path.join(dp, f)
                src[p] = io.open(p, encoding='utf-8-sig', errors='replace').read()
    return src


def scan(src, extra_text=None):
    """Return unregistered references by tool name. extra_text injects a self-test case."""
    names = set()
    for s in src.values():
        names |= set(re.findall(r'McpServerTool\(Name\s*=\s*"([A-Za-z0-9_]+)"', s))
    items = list(src.items())
    if extra_text:
        items.append(('<sentinel>', extra_text))
    bad = collections.defaultdict(list)
    for p, s in items:
        for m in LIT.finditer(s):
            text = ' '.join(x[1:-1] for x in PIECE.findall(m.group(1)))
            line = s[:m.start()].count('\n') + 1
            for t in set(TOK.findall(text)):
                if t in names or t in ALLOWED or not VERB.match(t):
                    continue
                bad[t].append(os.path.basename(p) + ':' + str(line))

    def flag(t, where):
        if t not in names and t not in ALLOWED and VERB.match(t):
            bad[t].append(where)

    for p in EXTRA_SOURCES:
        s = src.get(p)
        if s is None:
            continue
        for m in ANY_STR.finditer(s):
            line = s[:m.start()].count('\n') + 1
            for t in set(TOK.findall(m.group(0))):
                flag(t, os.path.basename(p) + ':' + str(line))
    if os.path.exists(SKILL_MD):
        md = io.open(SKILL_MD, encoding='utf-8-sig', errors='replace').read()
        for m in TICK.finditer(md):
            flag(m.group(1), 'SKILL.md:' + str(md[:m.start()].count('\n') + 1))
    return names, bad


def main():
    src = load(ROOT)
    if not src:
        print('Source directory %s not found. Run from the repository root.' % ROOT)
        return 2

    sentinel = '[McpServerTool(Name = "SentinelTool"), Description("Use GetNonexistentSentinelTool first.")]'
    _, caught = scan(src, extra_text=sentinel)
    if 'GetNonexistentSentinelTool' not in caught:
        print('[FAIL] The check missed the injected invalid tool name.')
        return 2

    names, bad = scan(src)
    print('Registered tools: %d. Files scanned: %d.' % (len(names), len(src)))
    if not bad:
        print('[PASS] All referenced tool names are registered.')
        return 0
    print('[FAIL] References to unregistered tools:')
    for t, locs in sorted(bad.items()):
        print('  %-38s %2d refs  %s' % (t, len(locs), ', '.join(sorted(set(locs))[:4])))
    print('Update the reference or register the tool. '
          'Add non-tool identifiers to ALLOWED with a reason.')
    return 1


if __name__ == '__main__':
    if '--selftest' in sys.argv:
        src = load(ROOT)
        _, caught = scan(src, extra_text='[Description("Use GetNonexistentSentinelTool first.")]')
        ok = 'GetNonexistentSentinelTool' in caught
        print('Self-test: ' + ('PASS (invalid name detected)' if ok else 'FAIL (invalid name missed)'))
        sys.exit(0 if ok else 1)
    sys.exit(main())
