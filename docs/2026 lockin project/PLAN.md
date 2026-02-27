# THE 2026 LOCK-IN PROJECT: SYSTEMS ARCHITECT FOUNDATION

> **Vision:** Build an unshakable CS foundation by connecting theoretical coursework to real systems engineering — turning every lecture into architecture intuition.

---

## Pillars of the Foundation

| # | Pillar | Course | Core Focus |
|---|--------|--------|------------|
| 1 | Computational Physics | Automata & Theory of Computation (CSC 307/309) | Finite Automata, NFA/DFA Equivalence, Pumping Lemma, Closure Properties |
| 2 | Mathematical Physics | Linear Algebra (MTS 203) | Eigenvalues/Eigenvectors, Canonical Forms, Spectral Theorem |
| 3 | Empirical Physics | Statistics | Probability Distributions, Regression, Monte Carlo/Bootstrapping |

---

## 1. The Computational Physics: Automata & Theory of Computation (CSC 307/309)

**Core Focus:** Finite Automata, NFA/DFA Equivalence, Pumping Lemma, Closure Properties.

### Systems Connections
- **OS Process Scheduling & Concurrency limits** — NFA → DFA state explosion mirrors the cost of non-determinism in concurrent process management.
- **Compiler Lexical Analysis (Regex) vs. Parsing limits (Pumping Lemma)** — Regex engines power tokenizers; the Pumping Lemma defines where regular expressions fail and parsers must take over.
- **Microservice Composability (Closure properties)** — Closure under union/intersection/complement maps directly to composing and decomposing service contracts.
- **Digital Hardware Optimization (DFA minimization)** — Minimized DFAs = fewer gates, smaller circuits, faster state machines in hardware.

---

## 2. The Mathematical Physics: Linear Algebra (MTS 203)

**Core Focus:** Eigenvalues/Eigenvectors, Canonical Forms, Spectral Theorem.

### Systems Connections
- **Distributed Systems** — Consensus algorithm convergence rates and PageRank both rely on eigenvalue analysis of transition matrices.
- **Control Theory** — System stability analysis; avoiding exponential blowup via Jordan Normal Forms.
- **Machine Learning & Data** — Dimensionality reduction (PCA), covariance matrices, and spectral methods underpin every modern ML pipeline.

---

## 3. The Empirical Physics: Statistics

**Core Focus:** Probability Distributions, Regression, Monte Carlo/Bootstrapping.

### Systems Connections
- **Queuing Theory** — Load balancer predictive modeling (M/M/1, M/M/c queues) for capacity planning.
- **Performance Benchmarking** — Confidence intervals, hypothesis testing, and regression for meaningful system benchmarks.
- **Randomized Algorithms & System Uncertainty** — Monte Carlo simulation, bootstrapping for estimating system behavior under uncertainty.

---

## Guiding Principle

> Every theorem is a design constraint. Every proof technique is a debugging strategy. Every mathematical structure is an architecture pattern waiting to be recognized.
