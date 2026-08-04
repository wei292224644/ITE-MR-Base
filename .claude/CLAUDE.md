<!-- CODEGRAPH_START -->
## CodeGraph

In repositories indexed by CodeGraph (a `.codegraph/` directory exists at the repo root), reach for it BEFORE grep/find or reading files when you need to understand or locate code:

- **MCP tool** (when available): `codegraph_explore` answers most code questions in one call — the relevant symbols' verbatim source plus the call paths between them, including dynamic-dispatch hops grep can't follow. Name a file or symbol in the query to read its current line-numbered source. If it's listed but deferred, load it by name via tool search.
- **Shell** (always works): `codegraph explore "<symbol names or question>"` prints the same output.

If there is no `.codegraph/` directory, skip CodeGraph entirely — indexing is the user's decision.
<!-- CODEGRAPH_END -->

## 决策原则：以架构最优为判据

技术决策按**结构是否正确**来判，不按**实现是否省事**来判。以架构师视角思考，不以"最短 diff"视角思考。

**在"改动最小"与"结构更正确"之间，选后者。** 用结构性理由论证选择，不用工作量论证。「照搬更快」不是理由；「照搬保住了可审阅性」才是理由，而且要能被更强的结构理由推翻。

**迁移既有代码的默认是行为等价**——让迁移与修 bug 保持可分离，这本身是结构理由。但下列情形下，重构优先于照搬：

- 行为依赖**隐式**因素：调用顺序、标志位被谁先清、`await` 会不会真的挂起
- 同一份代码因数据不同走出**不同语义**（而非不同结果）
- 跨平台会走出不同时序，且故障只能在真机上复现
- 信任边界缺输入校验、缺防止数据丢失的错误处理

**拒绝为省事而留下的形状**：把宿主事件总线的形状留在包里、为单一实现造接口、把一个决策藏在多个处理器的相互作用中、用"碰巧能跑"替代"明确规定"。

**每个偏离既有行为的决定，在 `design.md` 里记成一条编号决策**，写清：选了什么语义、替代方案是什么、为什么否决。偏离不记录等于没发生过。

**不确定时给选项 + 推荐，由人拍板。** 不要因为某条路更快就默认走它，也不要因为怕麻烦而不提更优解。
