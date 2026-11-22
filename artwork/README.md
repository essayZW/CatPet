# artwork —— 出图中间目录（不要提交生成物到大仓库，仅作暂存）

这个目录是**模型出图与工程素材之间的隔离带**：

```
artwork\
├── inbox\        ← 模型/你 把产物按目录结构放这里（idle\、say\a\ …）
├── masks\        ← 编辑遮罩（mask_eyes / mask_mouth / mask_head），已生成
├── processed\    ← import-artwork.ps1 归一化后的中间产物（脚本生成）
├── backup\       ← 每次导入前的旧素材备份（脚本生成）
└── gpt\gen.py    ← gpt-image-1 批量出图脚本
```

## 为什么要中间目录

- 出图是反复试错的过程，直接写工程目录会污染 `mod\`，也很难回滚；
- 导入前可以整体校验（尺寸/帧数/透明/命名），不合格就完全不碰工程；
- 每次导入都会自动备份旧素材，出问题能一键还原。

## 常用命令（绝对路径）

```powershell
# 出基准图
python D:\code\my_pet\artwork\gpt\gen.py base

# 出全部动作帧（跳过已存在的）
python D:\code\my_pet\artwork\gpt\gen.py frames --base D:\code\my_pet\CatPet\assets\base\cat-base.png

# 只出某个动作
python D:\code\my_pet\artwork\gpt\gen.py frames --base ... --only say

# 导入前干跑
powershell -ExecutionPolicy Bypass -File D:\code\my_pet\CatPet\tools\import-artwork.ps1 -WhatIf

# 正式导入（自动备份 + 归一化 + 校验）
powershell -ExecutionPolicy Bypass -File D:\code\my_pet\CatPet\tools\import-artwork.ps1
```

完整规格见 `D:\code\my_pet\docs\SKILL-猫咪桌宠素材.md`。
