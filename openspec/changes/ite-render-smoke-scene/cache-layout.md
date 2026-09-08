# ITE 内容缓存的落盘布局与准备步骤

离线跑 `IteRenderTest.unity` 需要手工摆好的内容缓存。缓存不进仓库（38 MB，且是远端产物），换机器要重摆一遍。

## 缓存根目录

`Application.persistentDataPath`。由 `ProjectSettings.asset` 的 `companyName` / `productName` 决定，两者当前都是 `响堂山`：

| 环境 | 路径 |
|---|---|
| macOS 编辑器 | `~/Library/Application Support/响堂山/响堂山` |
| Windows 编辑器 | `%USERPROFILE%\AppData\LocalLow\响堂山\响堂山` |
| Android 真机 | `/sdcard/Android/data/<applicationId>/files` |

## 目标布局

```
<persistentDataPath>/
├── IteSpaceScene_thirdDemo/          ← 空间场景。名字由 IteContentPipeline.SpaceSceneFolder() 决定
│   ├── thirdDemo.json                ← 已裁剪：tours 只留 wm0l5qcn_ibd 一个（见 design D4）
│   └── assets/
│       ├── logo.png
│       ├── wm0l5qcn_ibd.png          ← tour 预览图，按 tourID 命名
│       ├── 4kvhqwvp_12f.png
│       ├── azdugaax_xry.png
│       ├── earyserh_i5x.png
│       └── hkdaowxy_0hu.png
└── wm0l5qcn_ibd/                     ← tour 内容包，直接落在根下（不套文件夹）
    ├── wm0l5qcn_ibd.json
    └── asset/
        ├── anchor.jpg
        └── u2yp1sj/10/               ← {assetId}/{version}
            ├── publish.json          ← 其中 id = ysodu11w_dow，决定 glb 文件名
            ├── ysodu11w_dow_sceneViewer.glb
            └── 8xM3bgHTO.mp3
```

两类包对"顶层目录"的期望是**相反**的，摆的时候别搞混：

- 空间场景包解压到 `IteSpaceScene_{sceneName}/`，其内容**不再**多一层 `{sceneName}/`。
  （远端 `thirdDemo.zip` 里恰恰多了这一层——这正是 design D5 记录的 bug，手工摆时剥掉。）
- tour 包解压到 `persistentDataPath` **根**，其内容**必须**保留顶层 `{tourId}/`。
  （远端 tour 包本来就是这个形状，直接 `unzip -d <persistentDataPath>` 即可。）

## 准备步骤

```bash
P="$HOME/Library/Application Support/响堂山/响堂山"     # macOS 编辑器
mkdir -p "$P"

# 1. 空间场景包：下载、解压、剥掉多余的一层
curl -o /tmp/thirdDemo.zip https://ite-spatial-config.uality.cn/thirdDemo.zip
unzip -q /tmp/thirdDemo.zip -d /tmp/tds
mkdir -p "$P/IteSpaceScene_thirdDemo"
cp -R /tmp/tds/thirdDemo/assets "$P/IteSpaceScene_thirdDemo/"
cp /tmp/tds/thirdDemo/thirdDemo.json "$P/IteSpaceScene_thirdDemo/"

# 2. 裁剪 tour 列表，只留一个（否则 LoadAsync 会要求 5 个包全部就位）
python3 - "$P/IteSpaceScene_thirdDemo/thirdDemo.json" <<'PY'
import json, sys
p = sys.argv[1]
d = json.load(open(p))
d['tours'] = [t for t in d['tours'] if t['tourID'] == 'wm0l5qcn_ibd']
json.dump(d, open(p, 'w'), ensure_ascii=False, indent=2)
PY

# 3. tour 内容包：29 MB，解压到根，保留顶层目录
curl -o /tmp/wm0l5qcn_ibd.zip https://ite-pkg.uality.cn/wm0l5qcn_ibd/wm0l5qcn_ibd_wx.zip
unzip -q /tmp/wm0l5qcn_ibd.zip -d "$P"
```

## 验证

```bash
test -f "$P/IteSpaceScene_thirdDemo/thirdDemo.json" && \
test -f "$P/wm0l5qcn_ibd/wm0l5qcn_ibd.json" && \
test -f "$P/wm0l5qcn_ibd/asset/u2yp1sj/10/ysodu11w_dow_sceneViewer.glb" && echo OK
```

## 其余 tour（备用）

| tourID | displayType | 包大小 | 版本 |
|---|---|---|---|
| wm0l5qcn_ibd | regionalTrigger | 29 MB | 7 |
| earyserh_i5x | regionalTrigger | 31 MB | 17 |
| hkdaowxy_0hu | regionalTrigger | 37 MB | 9 |
| azdugaax_xry | regionalTrigger | 44 MB | 8 |
| 4kvhqwvp_12f | normal | **151 MB** | 24 |

包地址 `https://ite-pkg.uality.cn/{id}/{id}_wx.zip`。换 tour 时同步改缓存里 `thirdDemo.json` 的 tours 列表与场景中激活器的 tourId。

未裁剪的原始 `thirdDemo.json`（含 5 个 tour）可随时从 CDN 重新取。
