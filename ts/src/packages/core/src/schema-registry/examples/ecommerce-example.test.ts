import { describe, it, expect, beforeEach } from 'vitest';
import { SchemaRegistry, SchemaExecutor } from '../index.js';
import { InMemoryEventStore } from '../../events/in-memory-event-store.js';
import {
  // Events
  OrderCreated,
  OrderShipped,
  OrderDelivered,
  OrderCancelled,
  InventoryItemAdded,
  InventoryRestocked,
  InventoryReserved,
  InventoryReleased,
  
  // Commands
  CreateOrder,
  ShipOrder,
  AddInventoryItem,
  RestockInventory,
  
  // Projectors
  OrderProjector,
  InventoryProjector,
  
  // Queries
  LowStockQuery,
  OrdersByCustomerQuery
} from './ecommerce-domain.js';
import { PartitionKeys } from '../../documents/partition-keys.js';

describe('E-commerce Domain Example', () => {
  let registry: SchemaRegistry;
  let eventStore: InMemoryEventStore;
  let executor: SchemaExecutor;

  beforeEach(() => {
    // Setup registry
    registry = new SchemaRegistry();
    eventStore = new InMemoryEventStore();
    
    // Register all events
    registry.registerEvent(OrderCreated);
    registry.registerEvent(OrderShipped);
    registry.registerEvent(OrderDelivered);
    registry.registerEvent(OrderCancelled);
    registry.registerEvent(InventoryItemAdded);
    registry.registerEvent(InventoryRestocked);
    registry.registerEvent(InventoryReserved);
    registry.registerEvent(InventoryReleased);
    
    // Register commands
    registry.registerCommand(CreateOrder);
    registry.registerCommand(ShipOrder);
    registry.registerCommand(AddInventoryItem);
    registry.registerCommand(RestockInventory);
    
    // Register projectors
    registry.registerProjector(OrderProjector);
    registry.registerProjector(InventoryProjector);
    
    // Create executor
    executor = new SchemaExecutor({ registry, eventStore });
  });

  describe('Order Management', () => {
    it('creates an order successfully', async () => {
      // Arrange
      const orderData = {
        customerId: 'cust-123',
        items: [
          {
            productId: 'prod-1',
            productName: 'Laptop',
            quantity: 1,
            unitPrice: { amount: 999.99, currency: 'USD' }
          },
          {
            productId: 'prod-2',
            productName: 'Mouse',
            quantity: 2,
            unitPrice: { amount: 29.99, currency: 'USD' }
          }
        ],
        shippingAddress: {
          street: '123 Main St',
          city: 'New York',
          state: 'NY',
          zipCode: '10001',
          country: 'USA'
        }
      };

      // Act
      const result = await executor.executeCommand(CreateOrder, orderData);

      // Assert
      expect(result.isOk()).toBe(true);
      if (result.isOk()) {
        expect(result.value.success).toBe(true);
        expect(result.value.version).toBe(1);
        expect(result.value.eventIds).toHaveLength(1);
        
        // Query the created order
        const orderId = result.value.aggregateId;
        const queryResult = await executor.queryAggregate(
          PartitionKeys.existing(orderId, 'Order'),
          OrderProjector
        );
        
        expect(queryResult.isOk()).toBe(true);
        if (queryResult.isOk()) {
          const order = queryResult.value.data.payload as any;
          expect(order.customerId).toBe('cust-123');
          expect(order.items).toHaveLength(2);
          expect(order.totalAmount.amount).toBe(1059.97); // 999.99 + 2 * 29.99
          expect(order.status).toBe('confirmed');
        }
      }
    });

    it('ships an order', async () => {
      // Arrange - Create order first
      const orderData = {
        customerId: 'cust-123',
        items: [{
          productId: 'prod-1',
          productName: 'Laptop',
          quantity: 1,
          unitPrice: { amount: 999.99, currency: 'USD' }
        }],
        shippingAddress: {
          street: '123 Main St',
          city: 'New York',
          state: 'NY',
          zipCode: '10001',
          country: 'USA'
        }
      };
      
      const createResult = await executor.executeCommand(CreateOrder, orderData);
      expect(createResult.isOk()).toBe(true);
      const orderId = createResult._unsafeUnwrap().aggregateId;

      // Act - Ship the order
      const shipData = {
        orderId,
        trackingNumber: 'TRACK-12345',
        carrier: 'FedEx'
      };
      const shipResult = await executor.executeCommand(ShipOrder, shipData);

      // Assert
      expect(shipResult.isOk()).toBe(true);
      
      // Query the order to verify status
      const queryResult = await executor.queryAggregate(
        PartitionKeys.existing(orderId, 'Order'),
        OrderProjector
      );
      
      expect(queryResult.isOk()).toBe(true);
      if (queryResult.isOk()) {
        const order = queryResult.value.data.payload as any;
        expect(order.status).toBe('shipped');
        expect(order.trackingNumber).toBe('TRACK-12345');
        expect(order.carrier).toBe('FedEx');
      }
    });
  });

  describe('Inventory Management', () => {
    it('adds and restocks inventory', async () => {
      // Arrange - Add inventory item
      const addData = {
        productName: 'Laptop',
        sku: 'LAP-001',
        initialQuantity: 10,
        reorderLevel: 5,
        unitCost: { amount: 750.00, currency: 'USD' }
      };

      // Act - Add item
      const addResult = await executor.executeCommand(AddInventoryItem, addData);
      expect(addResult.isOk()).toBe(true);
      const productId = addResult._unsafeUnwrap().aggregateId;

      // Act - Restock
      const restockData = {
        productId,
        quantity: 20
      };
      const restockResult = await executor.executeCommand(RestockInventory, restockData);

      // Assert
      expect(restockResult.isOk()).toBe(true);
      
      // Query inventory
      const queryResult = await executor.queryAggregate(
        PartitionKeys.existing(productId, 'Inventory'),
        InventoryProjector
      );
      
      expect(queryResult.isOk()).toBe(true);
      if (queryResult.isOk()) {
        const inventory = queryResult.value.data.payload as any;
        expect(inventory.quantity).toBe(30); // 10 + 20
        expect(inventory.productName).toBe('Laptop');
        expect(inventory.sku).toBe('LAP-001');
      }
    });
  });

  describe('Multi-Projection Queries', () => {
    it('finds low stock items', async () => {
      // Arrange - Add some inventory items
      const products = [
        {
          productName: 'Laptop',
          sku: 'LAP-001',
          initialQuantity: 3, // Below reorder level
          reorderLevel: 5,
          unitCost: { amount: 750.00, currency: 'USD' }
        },
        {
          productName: 'Mouse',
          sku: 'MOU-001',
          initialQuantity: 50, // Above reorder level
          reorderLevel: 10,
          unitCost: { amount: 25.00, currency: 'USD' }
        },
        {
          productName: 'Keyboard',
          sku: 'KEY-001',
          initialQuantity: 2, // Below reorder level
          reorderLevel: 8,
          unitCost: { amount: 75.00, currency: 'USD' }
        }
      ];

      for (const product of products) {
        await executor.executeCommand(AddInventoryItem, product);
      }

      // Act - Query for low stock items
      const query = new LowStockQuery();
      const result = await executor.executeMultiProjectionQuery(query);

      // Assert
      if (result.isErr()) {
        console.error('Query failed:', result.error);
      }
      expect(result.isOk()).toBe(true);
      if (result.isOk()) {
        const lowStockItems = result.value.data;
        expect(lowStockItems).toHaveLength(2);
        
        const laptopItem = lowStockItems.find(i => i.productName === 'Laptop');
        expect(laptopItem).toBeDefined();
        expect(laptopItem?.currentQuantity).toBe(3);
        expect(laptopItem?.reorderLevel).toBe(5);
        
        const keyboardItem = lowStockItems.find(i => i.productName === 'Keyboard');
        expect(keyboardItem).toBeDefined();
        expect(keyboardItem?.currentQuantity).toBe(2);
        expect(keyboardItem?.reorderLevel).toBe(8);
      }
    });

    it('queries orders by customer', async () => {
      // Arrange - Create multiple orders for different customers
      const customer1Orders = [
        {
          customerId: 'cust-123',
          items: [{
            productId: 'prod-1',
            productName: 'Laptop',
            quantity: 1,
            unitPrice: { amount: 999.99, currency: 'USD' }
          }],
          shippingAddress: {
            street: '123 Main St',
            city: 'New York',
            state: 'NY',
            zipCode: '10001',
            country: 'USA'
          }
        },
        {
          customerId: 'cust-123',
          items: [{
            productId: 'prod-2',
            productName: 'Mouse',
            quantity: 2,
            unitPrice: { amount: 29.99, currency: 'USD' }
          }],
          shippingAddress: {
            street: '123 Main St',
            city: 'New York',
            state: 'NY',
            zipCode: '10001',
            country: 'USA'
          }
        }
      ];
      
      const customer2Order = {
        customerId: 'cust-456',
        items: [{
          productId: 'prod-3',
          productName: 'Keyboard',
          quantity: 1,
          unitPrice: { amount: 79.99, currency: 'USD' }
        }],
        shippingAddress: {
          street: '456 Oak Ave',
          city: 'Boston',
          state: 'MA',
          zipCode: '02101',
          country: 'USA'
        }
      };

      // Create orders
      for (const order of customer1Orders) {
        await executor.executeCommand(CreateOrder, order);
      }
      await executor.executeCommand(CreateOrder, customer2Order);

      // Act - Query orders for customer 1
      const query = new OrdersByCustomerQuery('cust-123');
      const result = await executor.executeMultiProjectionQuery(query);

      // Assert
      expect(result.isOk()).toBe(true);
      if (result.isOk()) {
        const orders = result.value.data;
        expect(orders).toHaveLength(2);
        
        // All orders should be for customer 1
        expect(orders.every(o => o.customerId === 'cust-123')).toBe(true);
        
        // Check order details
        const laptopOrder = orders.find(o => o.totalAmount.amount === 999.99);
        expect(laptopOrder).toBeDefined();
        expect(laptopOrder?.status).toBe('confirmed');
        
        const mouseOrder = orders.find(o => o.totalAmount.amount === 59.98);
        expect(mouseOrder).toBeDefined();
        expect(mouseOrder?.status).toBe('confirmed');
      }
    });
  });

  describe('Validation', () => {
    it('rejects orders with no items', async () => {
      // Arrange
      const invalidOrder = {
        customerId: 'cust-123',
        items: [],
        shippingAddress: {
          street: '123 Main St',
          city: 'New York',
          state: 'NY',
          zipCode: '10001',
          country: 'USA'
        }
      };

      // Act
      const result = await executor.executeCommand(CreateOrder, invalidOrder);

      // Assert
      expect(result.isErr()).toBe(true);
      if (result.isErr()) {
        expect(result.error.message).toContain('at least one item');
      }
    });

    it('rejects orders with duplicate products', async () => {
      // Arrange
      const invalidOrder = {
        customerId: 'cust-123',
        items: [
          {
            productId: 'prod-1',
            productName: 'Laptop',
            quantity: 1,
            unitPrice: { amount: 999.99, currency: 'USD' }
          },
          {
            productId: 'prod-1', // Duplicate product ID
            productName: 'Laptop',
            quantity: 1,
            unitPrice: { amount: 999.99, currency: 'USD' }
          }
        ],
        shippingAddress: {
          street: '123 Main St',
          city: 'New York',
          state: 'NY',
          zipCode: '10001',
          country: 'USA'
        }
      };

      // Act
      const result = await executor.executeCommand(CreateOrder, invalidOrder);

      // Assert
      expect(result.isErr()).toBe(true);
      if (result.isErr()) {
        expect(result.error.message).toContain('duplicate products');
      }
    });
  });
});