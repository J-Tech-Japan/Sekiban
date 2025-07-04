import { describe, it, expect } from 'vitest';
import { 
  SortableIdConditionNone,
  SinceSortableIdCondition,
  BetweenSortableIdCondition,
  SortableIdCondition
} from '../../events/event-retrieval-info.js';
import { SortableUniqueId } from '../../documents/sortable-unique-id.js';

describe('SortableIdConditionNone', () => {
  it('never filters out any ID', () => {
    const condition = new SortableIdConditionNone();
    const id1 = SortableUniqueId.generate();
    const id2 = SortableUniqueId.generate();
    
    expect(condition.outsideOfRange(id1)).toBe(false);
    expect(condition.outsideOfRange(id2)).toBe(false);
  });

  it('returns singleton instance', () => {
    const condition1 = SortableIdConditionNone.none;
    const condition2 = SortableIdConditionNone.none;
    
    expect(condition1).toBe(condition2);
  });
});

describe('SinceSortableIdCondition', () => {
  it('filters out IDs that are before or equal to the threshold', () => {
    const threshold = SortableUniqueId.generate();
    const condition = new SinceSortableIdCondition(threshold);
    
    // Same ID should be filtered out
    expect(condition.outsideOfRange(threshold)).toBe(true);
  });

  it('allows IDs that are after the threshold', () => {
    const earlier = SortableUniqueId.generate();
    // Wait a tiny bit to ensure the next ID is later
    const later = SortableUniqueId.generate();
    const condition = new SinceSortableIdCondition(earlier);
    
    expect(condition.outsideOfRange(later)).toBe(false);
  });

  it('filters out IDs that are before the threshold', () => {
    const earlier = SortableUniqueId.generate();
    const later = SortableUniqueId.generate();
    const condition = new SinceSortableIdCondition(later);
    
    expect(condition.outsideOfRange(earlier)).toBe(true);
  });
});

describe('BetweenSortableIdCondition', () => {
  let id1: SortableUniqueId;
  let id2: SortableUniqueId;
  let id3: SortableUniqueId;

  beforeEach(() => {
    // Create three IDs in sequence
    id1 = SortableUniqueId.generate();
    id2 = SortableUniqueId.generate();
    id3 = SortableUniqueId.generate();
  });

  it('includes IDs within the range', () => {
    const condition = new BetweenSortableIdCondition(id1, id3);
    
    expect(condition.outsideOfRange(id1)).toBe(false);
    expect(condition.outsideOfRange(id2)).toBe(false);
    expect(condition.outsideOfRange(id3)).toBe(false);
  });

  it('filters out IDs before the range', () => {
    const condition = new BetweenSortableIdCondition(id2, id3);
    
    expect(condition.outsideOfRange(id1)).toBe(true);
  });

  it('filters out IDs after the range', () => {
    const condition = new BetweenSortableIdCondition(id1, id2);
    
    expect(condition.outsideOfRange(id3)).toBe(true);
  });
});

describe('SortableIdCondition factory methods', () => {
  describe('none()', () => {
    it('returns a condition that never filters', () => {
      const condition = SortableIdCondition.none();
      const id = SortableUniqueId.generate();
      
      expect(condition.outsideOfRange(id)).toBe(false);
    });
  });

  describe('since()', () => {
    it('creates a since condition', () => {
      const threshold = SortableUniqueId.generate();
      const condition = SortableIdCondition.since(threshold);
      
      expect(condition).toBeInstanceOf(SinceSortableIdCondition);
      expect(condition.outsideOfRange(threshold)).toBe(true);
    });
  });

  describe('between()', () => {
    it('creates a between condition with correct order', () => {
      const earlier = SortableUniqueId.generate();
      const later = SortableUniqueId.generate();
      const condition = SortableIdCondition.between(earlier, later);
      
      expect(condition).toBeInstanceOf(BetweenSortableIdCondition);
    });

    it('swaps order when end is before start', () => {
      const earlier = SortableUniqueId.generate();
      const later = SortableUniqueId.generate();
      const condition = SortableIdCondition.between(later, earlier);
      
      // Should still include both IDs
      expect(condition.outsideOfRange(earlier)).toBe(false);
      expect(condition.outsideOfRange(later)).toBe(false);
    });
  });

  describe('fromState()', () => {
    it('returns none condition for null state', () => {
      const condition = SortableIdCondition.fromState(null);
      
      expect(condition).toBeInstanceOf(SortableIdConditionNone);
    });

    it('returns none condition for undefined state', () => {
      const condition = SortableIdCondition.fromState(undefined);
      
      expect(condition).toBeInstanceOf(SortableIdConditionNone);
    });

    it('returns since condition for state with lastSortableUniqueId', () => {
      const lastId = SortableUniqueId.generate();
      const state = {
        lastSortableUniqueId: lastId,
        // Other aggregate properties would be here
      } as any;
      
      const condition = SortableIdCondition.fromState(state);
      
      expect(condition).toBeInstanceOf(SinceSortableIdCondition);
      expect(condition.outsideOfRange(lastId)).toBe(true);
    });

    it('returns none condition for state without lastSortableUniqueId', () => {
      const state = {
        lastSortableUniqueId: null,
      } as any;
      
      const condition = SortableIdCondition.fromState(state);
      
      expect(condition).toBeInstanceOf(SortableIdConditionNone);
    });
  });
});