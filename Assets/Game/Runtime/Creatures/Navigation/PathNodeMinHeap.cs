using System.Collections.Generic;

namespace Supernova.MinecraftCaves.Creatures.Navigation
{
    /// <summary>
    /// Binary min heap over search node indices, ordered by total cost. Each entry
    /// remembers its slot so a cheaper route to an already queued node can sift it
    /// up in place rather than pushing a duplicate, the same decrease-key strategy
    /// Minecraft's PathMinHeap uses.
    /// </summary>
    public sealed class PathNodeMinHeap
    {
        private readonly List<int> heap = new List<int>();
        private IReadOnlyList<float> totalCosts;
        private int[] slots = new int[64];

        public int Count => heap.Count;
        public bool IsEmpty => heap.Count == 0;

        /// <summary>
        /// Points the heap at the cost array it should order by. The array is read
        /// live, so a caller lowering a cost must follow up with <see cref="Sift"/>.
        /// </summary>
        public void Begin(IReadOnlyList<float> costs, int capacity)
        {
            totalCosts = costs;
            heap.Clear();
            if (slots.Length < capacity)
            {
                slots = new int[capacity];
            }
            for (int i = 0; i < slots.Length; i++)
            {
                slots[i] = -1;
            }
        }

        public bool Contains(int nodeIndex)
        {
            return nodeIndex >= 0
                && nodeIndex < slots.Length
                && slots[nodeIndex] >= 0;
        }

        public void Push(int nodeIndex)
        {
            EnsureSlotCapacity(nodeIndex);
            heap.Add(nodeIndex);
            slots[nodeIndex] = heap.Count - 1;
            SiftUp(heap.Count - 1);
        }

        /// <summary>Restores order after the node's total cost was lowered.</summary>
        public void Sift(int nodeIndex)
        {
            if (Contains(nodeIndex))
            {
                SiftUp(slots[nodeIndex]);
            }
        }

        public int Pop()
        {
            int result = heap[0];
            slots[result] = -1;

            int last = heap.Count - 1;
            if (last == 0)
            {
                heap.RemoveAt(0);
                return result;
            }

            heap[0] = heap[last];
            slots[heap[0]] = 0;
            heap.RemoveAt(last);
            SiftDown(0);
            return result;
        }

        private void EnsureSlotCapacity(int nodeIndex)
        {
            if (nodeIndex < slots.Length)
            {
                return;
            }

            int capacity = slots.Length;
            while (capacity <= nodeIndex)
            {
                capacity *= 2;
            }

            var expanded = new int[capacity];
            for (int i = 0; i < expanded.Length; i++)
            {
                expanded[i] = i < slots.Length ? slots[i] : -1;
            }
            slots = expanded;
        }

        private void SiftUp(int position)
        {
            int current = position;
            while (current > 0)
            {
                int parent = (current - 1) / 2;
                if (totalCosts[heap[current]] >= totalCosts[heap[parent]])
                {
                    break;
                }

                Swap(current, parent);
                current = parent;
            }
        }

        private void SiftDown(int position)
        {
            int current = position;
            while (true)
            {
                int left = current * 2 + 1;
                if (left >= heap.Count)
                {
                    break;
                }

                int right = left + 1;
                int smallest = right < heap.Count
                    && totalCosts[heap[right]] < totalCosts[heap[left]]
                        ? right
                        : left;
                if (totalCosts[heap[smallest]] >= totalCosts[heap[current]])
                {
                    break;
                }

                Swap(current, smallest);
                current = smallest;
            }
        }

        private void Swap(int left, int right)
        {
            int temporary = heap[left];
            heap[left] = heap[right];
            heap[right] = temporary;
            slots[heap[left]] = left;
            slots[heap[right]] = right;
        }
    }
}
