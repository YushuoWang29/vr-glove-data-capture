# YCB 子集资产清单

## 来源与许可

- 官方数据页：<https://ycb-benchmarks.s3-website-us-east-1.amazonaws.com/>
- 数据许可：[Creative Commons Attribution 4.0 International](https://creativecommons.org/licenses/by/4.0/)
- 下载版本：Google scanner，16k textured mesh
- 下载日期：2026-07-16
- 仓库处理：每个归档只保留 `textured.obj`、`textured.mtl` 和 `texture_map.png`

## 原始下载归档

| YCB ID | 官方归档 | SHA-256 |
|---|---|---|
| `005_tomato_soup_can` | `005_tomato_soup_can_google_16k.tgz` | `264F28559475E8E548B20420A7B6DB944B4B27471039A73733D2C08A4BA2CC3D` |
| `025_mug` | `025_mug_google_16k.tgz` | `4B640AFC1BAAA940B66948D1E59426D8DA2C5A16558C7C7A118BBE28D0737DE5` |
| `055_baseball` | `055_baseball_google_16k.tgz` | `46D0FCD1B8B10622B0ADDE9FE4EB064DCD37DD236AF87FC095DFAF1D85419BD7` |

下载 URL 由官方数据页的对象表生成，格式为：

```text
http://ycb-benchmarks.s3-website-us-east-1.amazonaws.com/data/google/<YCB_ID>_google_16k.tgz
```

## 仓库内关键文件哈希

| YCB ID | 文件 | SHA-256 |
|---|---|---|
| `005_tomato_soup_can` | `textured.obj` | `0DA7D8CA01CAEFA1E85D1AAD5EB198AA2298ECA5C2A903DFB4C9D2FB0814C39D` |
| `005_tomato_soup_can` | `texture_map.png` | `221306EE224FA52E7D768999179C2442354ED8AFD0475B89B463261DC6FC7990` |
| `025_mug` | `textured.obj` | `2D40B407E9E97402DE788BEBAA703FF03D682DA95318DB5FF9BFA1AA083B3A57` |
| `025_mug` | `texture_map.png` | `1131A0EE591D59E0A77E55FF9D72AEAC07AF76A0F9ED09CBD01489498649EF1F` |
| `055_baseball` | `textured.obj` | `F9B719A13329AAFDD5CD9E42717A9420AF0B8E0D609670E0132529D4210C8696` |
| `055_baseball` | `texture_map.png` | `4441BF4CC4A0836CE90FA0509F8FE9FB6AF7BFAB6D4CCB4BD9BCD7F842512237` |

Unity 生成的 `.meta` 文件用于保持导入 GUID 稳定，不改变第三方数据许可。
