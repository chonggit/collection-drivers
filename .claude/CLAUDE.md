# 项目规范

## 代码规范

### 1. 注释规范
所有公开类型、属性、方法必须包含中文注释。

### 2. 枚举规范
所有枚举成员必须显式指定整数值，不允许依赖自动编号。

### 3. Git 提交
格式 `type: 中文描述`（如 `feat: 添加审批功能`）。

### 4. TDD 纪律（新业务逻辑代码必须遵守）
- 涉及领域逻辑或 EF Core 持久化 → 测试先行（RED→GREEN→REFACTOR）
- 纯机械性工作（脚手架、DTO、枚举、常量）→ 跳过 TDD，必须声明理由
- 无 RED 证据的生产代码视为违规，需回滚重做

### 5. 【强制】Spec 审计门禁（不得以任何理由跳过）

**触发条件**（任一即触发）：
- `brainstorming` 技能完成 Spec 编写后
- 创建或大幅修改 `docs/superpowers/specs/*.md` 后

**审计流程**：
```
编写 Spec → 启动 spec-auditor 子代理 → 修复问题重新提交 → 通过后展示给用户
```

**禁止**：
- 跳过 spec-auditor 直接向用户展示 Spec
- 用自我审查替代 spec-auditor
- 部分修复就重新提交

**违反后果**：
- 立即启动审计循环
- 发现的问题额外修复一轮
- 向用户承认违规

> 此规则覆盖 brainstorming 技能中的"self-review"步骤。当 brainstorming 技能要求你做"spec self-review"时，**跳过错，改为启动 spec-auditor 子代理**。

### 6. 设计原则
- 生成的方案、代码必须必须遵守`YAGNI`原则