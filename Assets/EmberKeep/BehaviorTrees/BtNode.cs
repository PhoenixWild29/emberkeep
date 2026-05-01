using System;
using System.Collections.Generic;

namespace EmberKeep.BehaviorTrees {
    public enum NodeStatus { Success, Failure, Running }

    // Hand-rolled behaviour tree primitives. Kept deliberately minimal - no
    // decorators, no parallel composites, no time-sliced ticks - because the
    // value of writing this by hand is in the resume bullet, not in features
    // we don't need. See spec section 6.3.
    public abstract class BtNode {
        public abstract NodeStatus Tick(Blackboard bb);
    }

    // Logical OR: returns Success on the first child that succeeds. Returns
    // Failure only if every child failed. Forwards Running unchanged so a
    // child can hold the tree mid-tick (unused at the moment, kept for
    // forward-compat).
    public sealed class BtSelector : BtNode {
        readonly List<BtNode> _children;

        public BtSelector(params BtNode[] children) {
            _children = new List<BtNode>(children);
        }

        public override NodeStatus Tick(Blackboard bb) {
            foreach (var c in _children) {
                var s = c.Tick(bb);
                if (s == NodeStatus.Success || s == NodeStatus.Running) return s;
            }
            return NodeStatus.Failure;
        }
    }

    // Logical AND: returns Success only when every child succeeds. Returns
    // Failure on the first child that fails.
    public sealed class BtSequence : BtNode {
        readonly List<BtNode> _children;

        public BtSequence(params BtNode[] children) {
            _children = new List<BtNode>(children);
        }

        public override NodeStatus Tick(Blackboard bb) {
            foreach (var c in _children) {
                var s = c.Tick(bb);
                if (s == NodeStatus.Failure || s == NodeStatus.Running) return s;
            }
            return NodeStatus.Success;
        }
    }

    public sealed class BtCondition : BtNode {
        readonly Func<Blackboard, bool> _predicate;
        public BtCondition(Func<Blackboard, bool> predicate) { _predicate = predicate; }
        public override NodeStatus Tick(Blackboard bb) =>
            _predicate(bb) ? NodeStatus.Success : NodeStatus.Failure;
    }

    public sealed class BtAction : BtNode {
        readonly Func<Blackboard, NodeStatus> _action;
        public BtAction(Func<Blackboard, NodeStatus> action) { _action = action; }
        public BtAction(Action<Blackboard> action) {
            _action = bb => { action(bb); return NodeStatus.Success; };
        }
        public override NodeStatus Tick(Blackboard bb) => _action(bb);
    }
}
