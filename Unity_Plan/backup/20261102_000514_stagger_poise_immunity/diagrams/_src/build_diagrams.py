import re, sys, os, asyncio
STYLE = '''<style>
text{font-family:"Malgun Gothic","Apple SD Gothic Neo","Noto Sans CJK KR",sans-serif}
.t{font-size:14px;fill:#3d3d3a}.ts{font-size:12px;fill:#73726c}.th{font-size:14px;font-weight:500;fill:#3d3d3a}
.box{fill:#F8F7F3;stroke:#B4B2A9}.arr{stroke:#73726c;stroke-width:1.5;fill:none}
.c-purple>rect,rect.c-purple{fill:#EEEDFE;stroke:#534AB7}.c-purple>.th{fill:#3C3489}.c-purple>.ts{fill:#534AB7}
.c-teal>rect,rect.c-teal{fill:#E1F5EE;stroke:#0F6E56}.c-teal>.th{fill:#085041}.c-teal>.ts{fill:#0F6E56}
.c-coral>rect,rect.c-coral{fill:#FAECE7;stroke:#993C1D}.c-coral>.th{fill:#712B13}.c-coral>.ts{fill:#993C1D}
</style>'''
def standalone(src, stamp):
    s = src.replace('var(--s)', '#73726c').replace('var(--b)', '#B4B2A9')
    s = s.replace('<svg width="100%"', '<svg xmlns="http://www.w3.org/2000/svg" width="680"')
    s = re.sub(r'<defs>', '<defs>' + STYLE, s, count=1)
    m = re.search(r'viewBox="0 0 680 (\d+)"', s); h = int(m.group(1))
    s = s.replace(f'viewBox="0 0 680 {h}"', f'height="{h+24}" viewBox="0 0 680 {h+24}"')
    s = re.sub(r'(</defs>)', r'\1<rect width="680" height="%d" fill="#ffffff"/>' % (h + 24), s, count=1)
    s = s.replace('</svg>', f'<text class="ts" x="640" y="{h+8}" text-anchor="end" dominant-baseline="central">{stamp}</text></svg>')
    return s
async def render(svg_path, png_path):
    from playwright.async_api import async_playwright
    async with async_playwright() as p:
        b = await p.chromium.launch()
        pg = await b.new_page(device_scale_factor=2)
        await pg.goto('file://' + os.path.abspath(svg_path))
        el = await pg.query_selector('svg')
        await el.screenshot(path=png_path)
        await b.close()
if __name__ == '__main__':
    src_dir, out_dir, stamp = sys.argv[1], sys.argv[2], sys.argv[3]
    os.makedirs(out_dir, exist_ok=True)
    for src, name in (('src_01.svg', '01_전체구조'), ('src_02.svg', '02_Step_실행순서')):
        s = standalone(open(os.path.join(src_dir, src), encoding='utf-8').read(), stamp)
        sp = os.path.join(out_dir, name + '.svg'); open(sp, 'w', encoding='utf-8').write(s)
        asyncio.run(render(sp, os.path.join(out_dir, name + '.png')))
        print('ok', sp)
