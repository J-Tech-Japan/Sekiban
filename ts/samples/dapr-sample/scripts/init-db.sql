-- Initialize database for Sekiban Dapr sample
-- This script creates the necessary tables for event sourcing

CREATE EXTENSION IF NOT EXISTS "uuid-ossp";

-- Events table for event sourcing
CREATE TABLE IF NOT EXISTS events (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    stream_id VARCHAR(255) NOT NULL,
    event_type VARCHAR(255) NOT NULL,
    event_data JSONB NOT NULL,
    metadata JSONB,
    version INTEGER NOT NULL,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    
    CONSTRAINT events_stream_version_unique UNIQUE (stream_id, version)
);

-- Snapshots table for aggregate snapshots
CREATE TABLE IF NOT EXISTS snapshots (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    stream_id VARCHAR(255) NOT NULL,
    aggregate_type VARCHAR(255) NOT NULL,
    aggregate_data JSONB NOT NULL,
    version INTEGER NOT NULL,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    
    CONSTRAINT snapshots_stream_unique UNIQUE (stream_id)
);

-- Projections table for read models
CREATE TABLE IF NOT EXISTS projections (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    projection_name VARCHAR(255) NOT NULL,
    partition_key VARCHAR(255) NOT NULL,
    projection_data JSONB NOT NULL,
    last_event_id UUID,
    version INTEGER NOT NULL DEFAULT 0,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    
    CONSTRAINT projections_name_partition_unique UNIQUE (projection_name, partition_key)
);

-- Indexes for performance
CREATE INDEX IF NOT EXISTS idx_events_stream_id ON events(stream_id);
CREATE INDEX IF NOT EXISTS idx_events_created_at ON events(created_at);
CREATE INDEX IF NOT EXISTS idx_events_event_type ON events(event_type);
CREATE INDEX IF NOT EXISTS idx_snapshots_stream_id ON snapshots(stream_id);
CREATE INDEX IF NOT EXISTS idx_projections_name ON projections(projection_name);
CREATE INDEX IF NOT EXISTS idx_projections_partition_key ON projections(partition_key);

-- Add some sample data for testing
INSERT INTO events (stream_id, event_type, event_data, metadata, version) VALUES
    ('user-sample-1', 'UserRegistered', 
     '{"id": "user-sample-1", "name": "Sample User", "email": "sample@example.com"}', 
     '{"timestamp": "2024-01-01T00:00:00Z", "causationId": "init", "correlationId": "init"}', 
     1)
ON CONFLICT DO NOTHING;